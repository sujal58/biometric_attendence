using Microsoft.AspNetCore.Mvc;
using TimeswatchAttendance.Web.Services;

namespace TimeswatchAttendance.Web.Controllers;

[ApiController]
[Route("api/device")]
public class DeviceController : ControllerBase
{
    private readonly DahuaDeviceService _device;

    public DeviceController(DahuaDeviceService device) => _device = device;

    /// <summary>Connectivity proof: online flag, serial number, software version, live door state.</summary>
    [HttpGet("status")]
    public ActionResult<DeviceStatusDto> GetStatus() => _device.GetStatus();
}
