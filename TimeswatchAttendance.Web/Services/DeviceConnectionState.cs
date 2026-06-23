namespace TimeswatchAttendance.Web.Services;

/// <summary>
/// Shared singleton holding the live status of the device, written by
/// <see cref="ZkAttendanceService"/> and read by the status API.
/// </summary>
public sealed class DeviceConnectionState
{
    public volatile bool Online;
    public string? SerialNo;
    public string? Firmware;
    public string? LastError;
    public DateTime? LastEventUtc;

    /// <summary>Total punches imported this process lifetime (updated via Interlocked).</summary>
    public long PunchesCaptured;
}

/// <summary>API response describing current device connectivity.</summary>
public sealed record DeviceStatusDto(
    bool Online,
    string? SerialNo,
    string? Firmware,
    long PunchesCaptured,
    DateTime? LastEventUtc,
    string? LastError);
