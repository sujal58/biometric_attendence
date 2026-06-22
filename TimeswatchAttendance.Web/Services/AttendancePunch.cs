namespace TimeswatchAttendance.Web.Services;

/// <summary>
/// Lightweight, fully-managed snapshot of a punch event, marshalled out of the native
/// callback struct so the (time-critical) callback can return immediately. Persisted by
/// the consumer loop in <see cref="DahuaDeviceService"/>.
/// </summary>
public sealed record AttendancePunch(
    string UserId,
    string? CardNo,
    int DoorNo,
    DateTime EventTimeUtc,
    string OpenMethod,
    bool Success,
    string? DeviceIp);
