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

        // Default design-time connection — local dev PostgreSQL
        var connectionString = "Host=localhost;Port=5432;Database=uav_system;Username=uav_admin;Password=dev";

        optionsBuilder.UseNpgsql(connectionString);

        return new DeviceDbContext(optionsBuilder.Options);
    }
}
