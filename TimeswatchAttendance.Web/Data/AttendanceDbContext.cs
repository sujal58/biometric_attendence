using Microsoft.EntityFrameworkCore;
using TimeswatchAttendance.Web.Models;

namespace TimeswatchAttendance.Web.Data;

public class AttendanceDbContext : DbContext
{
    public AttendanceDbContext(DbContextOptions<AttendanceDbContext> options) : base(options)
    {
    }

    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AttendanceRecord>(e =>
        {
            e.ToTable("attendance_records");

            // Same punch can be reported more than once on reconnect; de-dupe on these.
            e.HasIndex(x => new { x.UserId, x.EventTimeUtc, x.DoorNo }).IsUnique();
            e.HasIndex(x => x.EventTimeLocal);
        });
    }
}
