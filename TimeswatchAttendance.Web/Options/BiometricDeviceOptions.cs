namespace TimeswatchAttendance.Web.Options;

/// <summary>
/// Connection settings for the Timeswatch Bio-27 (Dahua NetSDK) device.
/// Bound from the "BiometricDevice" section of appsettings.json.
/// </summary>
public sealed class BiometricDeviceOptions
{
    public const string SectionName = "BiometricDevice";

    /// <summary>Device IP address on the LAN.</summary>
    public string Ip { get; set; } = "";

    /// <summary>Dahua SDK TCP port (default 37777).</summary>
    public int Port { get; set; } = 37777;

    public string Username { get; set; } = "admin";

    public string Password { get; set; } = "";

    /// <summary>How long to wait between login attempts while the device is unreachable.</summary>
    public int LoginRetrySeconds { get; set; } = 15;
}
