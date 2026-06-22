namespace TimeswatchAttendance.Web.Services;

/// <summary>
/// Shared singleton holding the live connection status of the device, written by
/// <see cref="DahuaDeviceService"/> and read by the status API.
/// </summary>
public sealed class DeviceConnectionState
{
    public volatile bool Online;
    public string? SerialNo;
    public string? SoftwareVersion;
    public string? LastError;
    public DateTime? LastEventUtc;

    /// <summary>Total punches captured this process lifetime (updated via Interlocked).</summary>
    public long PunchesCaptured;
}

/// <summary>API response describing current device connectivity.</summary>
public sealed record DeviceStatusDto(
    bool Online,
    string? SerialNo,
    string? SoftwareVersion,
    string DoorState,
    long PunchesCaptured,
    DateTime? LastEventUtc,
    string? LastError);
