using Microsoft.EntityFrameworkCore;
using UavSystem.DeviceService.Domain.Entities;
using UavSystem.DeviceService.Domain.Enums;

namespace UavSystem.DeviceService.Infrastructure.Persistence;

public sealed class DeviceDbContext : DbContext
{
    public DbSet<Device> Devices => Set<Device>();

    public DeviceDbContext(DbContextOptions<DeviceDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // NOTE: HasPostgresEnum<DeviceStatus> intentionally removed.
        // EF Core is configured with .HasConversion<string>() below,
        // which stores enum values as VARCHAR — no physical Postgres TYPE needed.
        // Physical ENUMs caused __EFMigrationsHistory conflicts across services.

        modelBuilder.Entity<Device>(entity =>
        {
            entity.ToTable("devices");

            entity.HasKey(e => e.DeviceId);
            entity.Property(e => e.DeviceId)
                  .HasColumnName("device_id")
                  .ValueGeneratedNever(); // BIGINT PK assigned by admin

            entity.Property(e => e.LocationName)
                  .HasColumnName("location_name")
                  .HasMaxLength(255)
                  .IsRequired();

            entity.Property(e => e.Status)
                  .HasColumnName("status")
                  .HasMaxLength(50)
                  .HasDefaultValue(DeviceStatus.Offline)
                  .HasConversion<string>();

            entity.Property(e => e.AssignedMonitorId)
                  .HasColumnName("assigned_monitor_id");

            entity.HasIndex(e => e.AssignedMonitorId)
                  .HasDatabaseName("idx_devices_monitor");

            entity.Property(e => e.ApiKeyHash)
                  .HasColumnName("api_key_hash")
                  .HasMaxLength(255)
                  .IsRequired();

            entity.Property(e => e.UpdatedAt)
                  .HasColumnName("updated_at")
                  .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
    }
}
