using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TimeswatchAttendance.Web.Data;

namespace TimeswatchAttendance.Web.Controllers;

[ApiController]
[Route("api/attendance")]
public class AttendanceController : ControllerBase
{
    private readonly AttendanceDbContext _db;

    public AttendanceController(AttendanceDbContext db) => _db = db;

    /// <summary>
    /// Lists captured punches, most recent first. Optionally filter by local calendar day.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly? date, [FromQuery] int take = 100, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 1000);

        IQueryable<Models.AttendanceRecord> query = _db.AttendanceRecords.AsNoTracking();

        if (date is DateOnly d)
        {
            var start = d.ToDateTime(TimeOnly.MinValue);
            var end = start.AddDays(1);
            query = query.Where(r => r.EventTimeLocal >= start && r.EventTimeLocal < end);
        }

        var rows = await query
            .OrderByDescending(r => r.EventTimeLocal)
            .Take(take)
            .ToListAsync(ct);

        return Ok(rows);
    }
}
