using Microsoft.EntityFrameworkCore;
using UavSystem.DeviceService.Domain.Entities;
using UavSystem.DeviceService.Domain.Interfaces;

namespace UavSystem.DeviceService.Infrastructure.Persistence.Repositories;

public sealed class DeviceRepository : IDeviceRepository
{
    private readonly DeviceDbContext _context;

    public DeviceRepository(DeviceDbContext context)
    {
        _context = context;
    }

    public async Task<Device?> GetByIdAsync(long deviceId, CancellationToken ct = default)
    {
        return await _context.Devices.FindAsync(new object[] { deviceId }, ct);
    }

    public async Task<IEnumerable<Device>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Devices
            .OrderBy(d => d.DeviceId)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Device>> GetByMonitorIdAsync(Guid monitorId, CancellationToken ct = default)
    {
        return await _context.Devices
            .Where(d => d.AssignedMonitorId == monitorId)
            .OrderBy(d => d.DeviceId)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Device device, CancellationToken ct = default)
    {
        await _context.Devices.AddAsync(device, ct);
    }

    public Task UpdateAsync(Device device, CancellationToken ct = default)
    {
        _context.Devices.Update(device);
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(long deviceId, CancellationToken ct = default)
    {
        var device = await GetByIdAsync(deviceId, ct);
        if (device is not null)
            _context.Devices.Remove(device);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }
}
