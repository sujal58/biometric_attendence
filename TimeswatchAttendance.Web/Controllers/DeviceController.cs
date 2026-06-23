using Microsoft.AspNetCore.Mvc;
using TimeswatchAttendance.Web.Services;

namespace TimeswatchAttendance.Web.Controllers;

[ApiController]
[Route("api/device")]
public class DeviceController : ControllerBase
{
    private readonly ZkAttendanceService _device;

    public DeviceController(ZkAttendanceService device) => _device = device;

    /// <summary>Connectivity proof: online flag, serial number, firmware, punches imported.</summary>
    [HttpGet("status")]
    public ActionResult<DeviceStatusDto> GetStatus() => _device.GetStatus();
}
