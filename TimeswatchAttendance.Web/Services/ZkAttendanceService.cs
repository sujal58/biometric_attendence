using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TimeswatchAttendance.Web.Data;
using TimeswatchAttendance.Web.Models;
using TimeswatchAttendance.Web.Options;
using zkteco_attendance_api;

namespace TimeswatchAttendance.Web.Services;

/// <summary>
/// Background service that polls the ZKTeco (Timeswatch Bio-27) device for attendance punches
/// and imports new ones into MySQL. The device buffers its own logs, so polling every few
/// seconds never misses a punch. A high-water mark (latest imported timestamp) avoids re-importing.
/// </summary>
public sealed class ZkAttendanceService : BackgroundService
{
    private readonly BiometricDeviceOptions _opt;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DeviceConnectionState _state;
    private readonly ILogger<ZkAttendanceService> _logger;

    private DateTime _watermark = DateTime.MinValue;

    public ZkAttendanceService(
        IOptions<BiometricDeviceOptions> opt,
        IServiceScopeFactory scopeFactory,
        DeviceConnectionState state,
        ILogger<ZkAttendanceService> logger)
    {
        _opt = opt.Value;
        _scopeFactory = scopeFactory;
        _state = state;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _watermark = await GetLatestStoredTimestampAsync(stoppingToken);
        _logger.LogInformation(
            "ZK attendance poller started for {Ip}:{Port} every {Sec}s. Importing punches after {Watermark}.",
            _opt.Ip, _opt.Port, _opt.PollSeconds, _watermark == DateTime.MinValue ? "(all stored logs)" : _watermark.ToString());

        var interval = TimeSpan.FromSeconds(Math.Max(2, _opt.PollSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _state.Online = false;
                _state.LastError = ex.Message;
                _logger.LogWarning(ex, "ZK poll cycle failed; will retry.");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        var zk = new ZkTeco(_opt.Ip, _opt.Port, _opt.UseTcp);
        try
        {
            if (!zk.Connect(_opt.Password))
            {
                _state.Online = false;
                _state.LastError = $"Could not connect to {_opt.Ip}:{_opt.Port}";
                _logger.LogWarning("ZK connect failed at {Ip}:{Port}.", _opt.Ip, _opt.Port);
                return;
            }

            _state.Online = true;
            _state.LastError = null;
            try { _state.SerialNo = zk.GetDeviceSerial(); _state.Firmware = zk.GetFirmwareVersion(); }
            catch { /* device-info is best-effort */ }

            var logs = zk.GetAttendance() ?? new List<ZkTecoAttendance>();
            var fresh = logs.Where(r => r.Timestamp > _watermark).OrderBy(r => r.Timestamp).ToList();
            if (fresh.Count == 0) return;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();

            var inserted = 0;
            foreach (var r in fresh)
            {
                var local = r.Timestamp;
                var utc = DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
                var userId = r.UserId ?? string.Empty;

                bool exists = await db.AttendanceRecords.AnyAsync(
                    x => x.UserId == userId && x.EventTimeUtc == utc && x.DoorNo == r.Punch, ct);
                if (exists) continue;

                db.AttendanceRecords.Add(new AttendanceRecord
                {
                    UserId = userId,
                    CardNo = null,
                    DoorNo = r.Punch,                 // ZK "punch" = in/out direction
                    EventTimeUtc = utc,
                    EventTimeLocal = local,           // ZK device timestamps are device-local
                    OpenMethod = VerifyModeName(r.Status),
                    Success = true,                   // a stored ZK log is a successful verification
                    DeviceIp = _opt.Ip,
                    CreatedAtUtc = DateTime.UtcNow
                });
                inserted++;
            }

            if (inserted > 0) await db.SaveChangesAsync(ct);
            _watermark = fresh[^1].Timestamp;

            if (inserted > 0)
            {
                Interlocked.Add(ref _state.PunchesCaptured, inserted);
                _state.LastEventUtc = DateTime.UtcNow;
                _logger.LogInformation("Imported {Count} new punch(es); latest {Time}.", inserted, _watermark);
            }
        }
        finally
        {
            try { zk.Disconnect(); } catch { /* ignore */ }
        }
    }

    private async Task<DateTime> GetLatestStoredTimestampAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            if (await db.AttendanceRecords.AnyAsync(ct))
                return await db.AttendanceRecords.MaxAsync(x => x.EventTimeLocal, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not seed watermark from DB (table may not exist yet).");
        }
        return DateTime.MinValue;
    }

    public DeviceStatusDto GetStatus() =>
        new(_state.Online, _state.SerialNo, _state.Firmware,
            Interlocked.Read(ref _state.PunchesCaptured), _state.LastEventUtc, _state.LastError);

    // Best-effort label for the ZK verify mode (varies by firmware).
    private static string VerifyModeName(int status) => status switch
    {
        0 => "Password",
        1 => "Fingerprint",
        2 => "Card",
        15 => "Face",
        _ => $"Verify({status})"
    };
}
