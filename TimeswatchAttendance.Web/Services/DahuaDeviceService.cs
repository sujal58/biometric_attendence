using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetSDKCS;
using TimeswatchAttendance.Web.Data;
using TimeswatchAttendance.Web.Models;
using TimeswatchAttendance.Web.Options;

namespace TimeswatchAttendance.Web.Services;

/// <summary>
/// Long-lived hosted service that owns the single Dahua NetSDK session for the whole process.
/// Lifecycle: Init -> SetAutoReconnect -> SetDVRMessCallBack -> Login -> (read serial/version) -> StartListen.
/// Punches arrive on the native alarm callback, are marshalled to <see cref="AttendancePunch"/>,
/// queued on a Channel, and persisted by <see cref="ConsumeAsync"/> on a separate worker.
/// </summary>
public sealed class DahuaDeviceService : BackgroundService
{
    // ----- callback plumbing (MUST be static fields so the GC never collects the delegates
    //       while native code still holds them; see manual 2.1.1 / 2.1.6) -----
    private static fDisConnectCallBack? _disconnectCb;
    private static fHaveReConnectCallBack? _reconnectCb;
    private static fMessCallBack? _alarmCb;

    private static ILogger? _log;
    private static DeviceConnectionState? _state;
    private static ChannelWriter<AttendancePunch>? _punchWriter;
    private static string? _deviceIp;

    private readonly BiometricDeviceOptions _opt;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DeviceConnectionState _connState;
    private readonly ILogger<DahuaDeviceService> _logger;

    private readonly object _sdkLock = new();   // serialises our outbound SDK calls on the login session
    private readonly Channel<AttendancePunch> _channel =
        Channel.CreateUnbounded<AttendancePunch>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private IntPtr _loginId = IntPtr.Zero;

    public DahuaDeviceService(
        IOptions<BiometricDeviceOptions> opt,
        IServiceScopeFactory scopeFactory,
        DeviceConnectionState connState,
        ILogger<DahuaDeviceService> logger)
    {
        _opt = opt.Value;
        _scopeFactory = scopeFactory;
        _connState = connState;
        _logger = logger;

        // Wire up statics used by the native callbacks.
        _log = logger;
        _state = connState;
        _punchWriter = _channel.Writer;
        _deviceIp = _opt.Ip;

        _disconnectCb = OnDisconnect;
        _reconnectCb = OnReconnect;
        _alarmCb = OnAlarm;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // 1) Initialise the SDK once for the process.
            NETClient.Init(_disconnectCb, IntPtr.Zero, null);
            ConfigureSdkDiagnostics();
            NETClient.SetAutoReconnect(_reconnectCb, IntPtr.Zero);
            // 2) Register the global alarm callback BEFORE listening (manual 2.1.6 / 2.2.2).
            NETClient.SetDVRMessCallBack(_alarmCb, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NetSDK initialisation failed. Are the native DLLs (dhnetsdk.dll, ...) next to the app and is this an x64 process?");
            return;
        }

        // Run the DB consumer and the login/keep-alive loop together until shutdown.
        var consumer = ConsumeAsync(stoppingToken);
        var login = LoginLoopAsync(stoppingToken);
        await Task.WhenAll(consumer, login);
    }

    private async Task LoginLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _loginId == IntPtr.Zero)
        {
            IntPtr id = TryLogin(out var method, out var error);

            if (id == IntPtr.Zero)
            {
                _connState.LastError = error;
                _logger.LogWarning("Device login failed ({Ip}:{Port}): {Error}. Retrying in {Seconds}s.",
                    _opt.Ip, _opt.Port, error, _opt.LoginRetrySeconds);
                try { await Task.Delay(TimeSpan.FromSeconds(_opt.LoginRetrySeconds), ct); }
                catch (OperationCanceledException) { return; }
                continue;
            }

            _loginId = id;
            _logger.LogInformation("Login succeeded via {Method}.", method);
            ReadVersion(id);

            lock (_sdkLock) { NETClient.StartListen(id); }

            _connState.Online = true;
            _connState.LastError = null;
            _logger.LogInformation("Device ONLINE. Serial={Serial} Version={Version}. Listening for attendance punches.",
                _connState.SerialNo, _connState.SoftwareVersion);
        }
    }

    private void ConfigureSdkDiagnostics()
    {
        // Bump timeouts (defaults are short: ~5s login wait / 1.5s connect) for slower / Wi-Fi devices.
        var p = new NET_PARAM
        {
            nWaittime = 10000,
            nConnectTime = 8000,
            nConnectTryNum = 1,
            bReserved = new byte[4]
        };
        NETClient.SetNetworkParam(p);

        // Turn on the NetSDK's own protocol log so we can see exactly why a login is rejected.
        try
        {
            var logInfo = new NET_LOG_SET_PRINT_INFO
            {
                dwSize = (uint)Marshal.SizeOf(typeof(NET_LOG_SET_PRINT_INFO)),
                bSetFilePath = 1,
                szLogFilePath = "sdk_log/sdk_log.log"
            };
            if (NETClient.LogOpen(logInfo))
            {
                _logger.LogInformation("NetSDK diagnostic log enabled at <app folder>/sdk_log/sdk_log.log");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not enable NetSDK log.");
        }
    }

    /// <summary>
    /// Tries the modern secure login first, then the legacy LoginEx2. Older / OEM firmware sometimes
    /// times out on the high-security handshake but accepts the legacy login. Returns Zero on failure.
    /// </summary>
    private IntPtr TryLogin(out string method, out string? error)
    {
        lock (_sdkLock)
        {
            var devInfo = new NET_DEVICEINFO_Ex();
            var id = NETClient.LoginWithHighLevelSecurity(
                _opt.Ip, (ushort)_opt.Port, _opt.Username, _opt.Password,
                EM_LOGIN_SPAC_CAP_TYPE.TCP, IntPtr.Zero, ref devInfo);
            if (id != IntPtr.Zero) { method = "HighLevelSecurity"; error = null; return id; }
            var highLevelErr = NETClient.GetLastError();

            devInfo = new NET_DEVICEINFO_Ex();
            id = NETClient.Login(
                _opt.Ip, (ushort)_opt.Port, _opt.Username, _opt.Password,
                EM_LOGIN_SPAC_CAP_TYPE.TCP, IntPtr.Zero, ref devInfo);
            if (id != IntPtr.Zero) { method = "LoginEx2 (legacy)"; error = null; return id; }
            var legacyErr = NETClient.GetLastError();

            method = "none";
            error = $"high-level: {highLevelErr} | legacy: {legacyErr}";
            return IntPtr.Zero;
        }
    }

    private void ReadVersion(IntPtr loginId)
    {
        try
        {
            var version = new NET_DEV_VERSION_INFO();
            object obj = version;
            bool ok;
            lock (_sdkLock)
            {
                ok = NETClient.QueryDevState(loginId, EM_DEVICE_STATE.SOFTWARE, ref obj, typeof(NET_DEV_VERSION_INFO), 5000);
            }
            if (ok)
            {
                version = (NET_DEV_VERSION_INFO)obj;
                _connState.SerialNo = version.szDevSerialNo;
                _connState.SoftwareVersion = version.szSoftWareVersion;
            }
            else
            {
                _logger.LogWarning("QueryDevState(SOFTWARE) failed: {Error}", NETClient.GetLastError());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read device version/serial.");
        }
    }

    /// <summary>Queries the live door-contact state on demand (used by the status API).</summary>
    public string QueryDoorState()
    {
        if (_loginId == IntPtr.Zero) return "Offline";
        try
        {
            var info = new NET_DOOR_STATUS_INFO
            {
                dwSize = (uint)Marshal.SizeOf(typeof(NET_DOOR_STATUS_INFO)),
                nChannel = 0
            };
            object obj = info;
            bool ok;
            lock (_sdkLock)
            {
                ok = NETClient.QueryDevState(_loginId, EM_DEVICE_STATE.DOOR_STATE, ref obj, typeof(NET_DOOR_STATUS_INFO), 3000);
            }
            if (!ok) return "Unknown";
            info = (NET_DOOR_STATUS_INFO)obj;
            return info.emStateType.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "QueryDoorState failed.");
            return "Unknown";
        }
    }

    public DeviceStatusDto GetStatus()
    {
        var door = _connState.Online ? QueryDoorState() : "Offline";
        return new DeviceStatusDto(
            _connState.Online,
            _connState.SerialNo,
            _connState.SoftwareVersion,
            door,
            Interlocked.Read(ref _connState.PunchesCaptured),
            _connState.LastEventUtc,
            _connState.LastError);
    }

    // ---------------- consumer: persist queued punches ----------------
    private async Task ConsumeAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var punch in _channel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();

                    bool exists = await db.AttendanceRecords.AnyAsync(
                        r => r.UserId == punch.UserId && r.EventTimeUtc == punch.EventTimeUtc && r.DoorNo == punch.DoorNo,
                        ct);

                    if (!exists)
                    {
                        db.AttendanceRecords.Add(new AttendanceRecord
                        {
                            UserId = punch.UserId,
                            CardNo = punch.CardNo,
                            DoorNo = punch.DoorNo,
                            EventTimeUtc = punch.EventTimeUtc,
                            EventTimeLocal = punch.EventTimeUtc.ToLocalTime(),
                            OpenMethod = punch.OpenMethod,
                            Success = punch.Success,
                            DeviceIp = punch.DeviceIp,
                            CreatedAtUtc = DateTime.UtcNow
                        });
                        await db.SaveChangesAsync(ct);
                        _logger.LogInformation("Punch saved: User={User} Card={Card} Door={Door} At(UTC)={Time} Ok={Ok} Method={Method}",
                            punch.UserId, punch.CardNo, punch.DoorNo, punch.EventTimeUtc, punch.Success, punch.OpenMethod);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to persist attendance punch.");
                }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    // ---------------- native callbacks (static) ----------------
    private static bool OnAlarm(int lCommand, IntPtr lLoginID, IntPtr pBuf, uint dwBufLen,
        IntPtr pchDVRIP, int nDVRPort, IntPtr dwUser)
    {
        try
        {
            if ((EM_ALARM_TYPE)lCommand == EM_ALARM_TYPE.ALARM_ACCESS_CTL_EVENT)
            {
                var info = (NET_ALARM_ACCESS_CTL_EVENT_INFO)Marshal.PtrToStructure(
                    pBuf, typeof(NET_ALARM_ACCESS_CTL_EVENT_INFO))!;

                DateTime utc;
                var t = info.stuTime;
                try
                {
                    utc = new DateTime((int)t.dwYear, (int)t.dwMonth, (int)t.dwDay,
                        (int)t.dwHour, (int)t.dwMinute, (int)t.dwSecond, DateTimeKind.Utc);
                }
                catch
                {
                    utc = DateTime.UtcNow; // device sent an out-of-range timestamp
                }

                var punch = new AttendancePunch(
                    info.szUserID ?? string.Empty,
                    string.IsNullOrEmpty(info.szCardNo) ? null : info.szCardNo,
                    info.nDoor,
                    utc,
                    info.emOpenMethod.ToString(),
                    info.bStatus,
                    _deviceIp);

                _punchWriter?.TryWrite(punch);

                if (_state != null)
                {
                    _state.LastEventUtc = DateTime.UtcNow;
                    Interlocked.Increment(ref _state.PunchesCaptured);
                }
            }
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Error handling device alarm callback.");
        }

        // Do NOT do heavy/blocking work here (manual 2.1.6): we only marshal + enqueue.
        return true;
    }

    private static void OnDisconnect(IntPtr lLoginID, IntPtr pchDVRIP, int nDVRPort, IntPtr dwUser)
    {
        if (_state != null) _state.Online = false;
        _log?.LogWarning("Device DISCONNECTED. Auto-reconnect will retry.");
    }

    private static void OnReconnect(IntPtr lLoginID, IntPtr pchDVRIP, int nDVRPort, IntPtr dwUser)
    {
        if (_state != null) _state.Online = true;
        // Alarm subscription resumes automatically after reconnect (manual 2.1.1).
        _log?.LogInformation("Device RECONNECTED.");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_loginId != IntPtr.Zero)
            {
                lock (_sdkLock)
                {
                    NETClient.StopListen(_loginId);
                    NETClient.Logout(_loginId);
                }
                _loginId = IntPtr.Zero;
            }
            NETClient.Cleanup();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during NetSDK shutdown.");
        }
        finally
        {
            _connState.Online = false;
            _channel.Writer.TryComplete();
        }

        await base.StopAsync(cancellationToken);
    }
}
