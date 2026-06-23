using Microsoft.EntityFrameworkCore;
using TimeswatchAttendance.Web.Data;
using TimeswatchAttendance.Web.Options;
using TimeswatchAttendance.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Device connection settings (appsettings.json -> "BiometricDevice").
builder.Services.Configure<BiometricDeviceOptions>(
    builder.Configuration.GetSection(BiometricDeviceOptions.SectionName));

// MySQL via Pomelo. Pinned server version so startup does not need a live DB connection.
var connectionString = builder.Configuration.GetConnectionString("AttendanceDb")
    ?? throw new InvalidOperationException("Missing connection string 'AttendanceDb'.");
builder.Services.AddDbContext<AttendanceDbContext>(opt =>
    opt.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36))));

// One ZK poller for the whole process: registered as a singleton AND as the hosted service
// so controllers can read live status from the same instance.
builder.Services.AddSingleton<DeviceConnectionState>();
builder.Services.AddSingleton<ZkAttendanceService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ZkAttendanceService>());

var app = builder.Build();

// Simple bootstrap of the attendance schema. Swap for EF Core migrations once you want versioning:
//   dotnet ef migrations add Initial  &&  dotnet ef database update
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
    try
    {
        db.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Could not ensure the attendance database exists. Is MySQL running and reachable?");
    }
}

app.MapGet("/", () => Results.Content(
    "Timeswatch Bio-27 (ZKTeco) integration is running.\n" +
    "  GET /api/device/status   - connectivity, serial, firmware, punches imported\n" +
    "  GET /api/attendance       - recent punches (optional ?date=yyyy-MM-dd&take=100)\n",
    "text/plain"));

app.MapControllers();

app.Run();
