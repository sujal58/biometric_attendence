using System.ComponentModel.DataAnnotations;

namespace TimeswatchAttendance.Web.Models;

/// <summary>
/// One access-control / attendance punch captured from the device.
/// </summary>
public class AttendanceRecord
{
    public int Id { get; set; }

    /// <summary>Person/user id enrolled on the device (szUserID).</summary>
    [MaxLength(64)]
    public string UserId { get; set; } = "";

    /// <summary>Card number, if the punch came from a card (szCardNo).</summary>
    [MaxLength(64)]
    public string? CardNo { get; set; }

    /// <summary>Door channel number (nDoor).</summary>
    public int DoorNo { get; set; }

    /// <summary>Event time as reported by the device (UTC).</summary>
    public DateTime EventTimeUtc { get; set; }

    /// <summary>Event time converted to server local time (for display/filtering).</summary>
    public DateTime EventTimeLocal { get; set; }

    /// <summary>How the door was opened: Card, Fingerprint, Face, Password, etc. (emOpenMethod).</summary>
    [MaxLength(32)]
    public string OpenMethod { get; set; } = "";

    /// <summary>Whether the verification succeeded (bStatus).</summary>
    public bool Success { get; set; }

    [MaxLength(64)]
    public string? DeviceIp { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
