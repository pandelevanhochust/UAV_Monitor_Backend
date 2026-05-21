using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UavSystem.DeviceService.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for EF Core CLI tools (dotnet ef migrations).
/// Only used during migration generation — never at runtime.
/// </summary>
public sealed class DeviceDbContextDesignTimeFactory : IDesignTimeDbContextFactory<DeviceDbContext>
{
    public DeviceDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DeviceDbContext>();

        // Default design-time connection — local dev PostgreSQL (Docker exposed on 127.0.0.1:5433)
        var connectionString = "Host=127.0.0.1;Port=5433;Database=uav_device_db;Username=uav_admin;Password=hust_uav_secure_pass_2026;Include Error Detail=true;";

        optionsBuilder.UseNpgsql(connectionString);

        return new DeviceDbContext(optionsBuilder.Options);
    }
}
