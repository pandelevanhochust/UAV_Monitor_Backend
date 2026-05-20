namespace UavSystem.DeviceService.Application.Interfaces;

/// <summary>
/// Write-Through cache sync contract. Defined in Application, implemented in Infrastructure.
/// Called by every Command handler after PostgreSQL write.
/// </summary>
public interface IRedisDeviceSyncService
{
    Task SyncDeviceMetadataAsync(long deviceId, string status, Guid? monitorId,
        string location, string apiKeyHash, CancellationToken ct = default);
    Task SyncMonitorDevicesAsync(Guid monitorId, long deviceId, CancellationToken ct = default);
    Task RemoveMonitorDeviceAsync(Guid monitorId, long deviceId, CancellationToken ct = default);
    Task UpdateDeviceStatusAsync(long deviceId, string status, CancellationToken ct = default);
    Task RefreshHeartbeatAsync(long deviceId, CancellationToken ct = default);
}
