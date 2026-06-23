namespace TimeswatchAttendance.Web.Options;

/// <summary>
/// Connection settings for the Timeswatch Bio-27 (ZKTeco protocol).
/// Bound from the "BiometricDevice" section of appsettings.json.
/// </summary>
public sealed class BiometricDeviceOptions
{
    public const string SectionName = "BiometricDevice";

    /// <summary>Device IP address on the LAN.</summary>
    public string Ip { get; set; } = "";

    /// <summary>ZKTeco comm/SDK port. Default for ZK is 4370; this device is set to 5005.</summary>
    public int Port { get; set; } = 5005;

    /// <summary>ZKTeco comm key / device password (0 = none).</summary>
    public int Password { get; set; } = 0;

    /// <summary>Use TCP (true) or UDP (false). TCP is the norm.</summary>
    public bool UseTcp { get; set; } = true;

    /// <summary>How often to poll the device for new punches, in seconds.</summary>
    public int PollSeconds { get; set; } = 5;
}
