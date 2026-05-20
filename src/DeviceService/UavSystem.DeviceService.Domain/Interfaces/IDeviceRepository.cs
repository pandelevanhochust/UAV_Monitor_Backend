using UavSystem.DeviceService.Domain.Entities;

namespace UavSystem.DeviceService.Domain.Interfaces;

/// <summary>
/// Repository contract for Device aggregate. Defined in Domain, implemented in Infrastructure.
/// </summary>
public interface IDeviceRepository
{
    Task<Device?> GetByIdAsync(long deviceId, CancellationToken ct = default);
    Task<IEnumerable<Device>> GetAllAsync(CancellationToken ct = default);
    Task<IEnumerable<Device>> GetByMonitorIdAsync(Guid monitorId, CancellationToken ct = default);
    Task AddAsync(Device device, CancellationToken ct = default);
    Task UpdateAsync(Device device, CancellationToken ct = default);
    Task DeleteAsync(long deviceId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
