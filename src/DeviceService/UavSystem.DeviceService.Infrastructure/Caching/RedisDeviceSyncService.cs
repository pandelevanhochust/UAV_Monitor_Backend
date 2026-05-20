using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using UavSystem.DeviceService.Application.Interfaces;
using UavSystem.Shared.Infrastructure.Caching;

namespace UavSystem.DeviceService.Infrastructure.Caching;

/// <summary>
/// Write-Through cache sync service. Called by every Command handler
/// after PostgreSQL write. Uses IDatabase.HashSetAsync and SetAddAsync.
/// Redis must NEVER serve stale device metadata (Appendix C Rule 7).
/// </summary>
public sealed class RedisDeviceSyncService : IRedisDeviceSyncService
{
    private readonly IDatabase _redis;
    private readonly ILogger<RedisDeviceSyncService> _logger;

    public RedisDeviceSyncService(IConnectionMultiplexer connectionMultiplexer, ILogger<RedisDeviceSyncService> logger)
    {
        _redis = connectionMultiplexer.GetDatabase();
        _logger = logger;
    }

    public async Task SyncDeviceMetadataAsync(
        long deviceId, string status, Guid? monitorId,
        string location, string apiKeyHash, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var key = RedisKeys.DeviceMeta(deviceId);
        var entries = new HashEntry[]
        {
            new("status", status),
            new("monitor_id", monitorId?.ToString() ?? string.Empty),
            new("location", location),
            new("api_key_hash", apiKeyHash)
        };

        await _redis.HashSetAsync(key, entries);
        _logger.LogDebug("Redis Write-Through: synced device:meta:{DeviceId}", deviceId);
    }

    public async Task SyncMonitorDevicesAsync(Guid monitorId, long deviceId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var key = RedisKeys.MonitorDevices(monitorId);
        await _redis.SetAddAsync(key, deviceId.ToString());
        _logger.LogDebug("Redis Write-Through: added device {DeviceId} to monitor:devices:{MonitorId}",
            deviceId, monitorId);
    }

    public async Task RemoveMonitorDeviceAsync(Guid monitorId, long deviceId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var key = RedisKeys.MonitorDevices(monitorId);
        await _redis.SetRemoveAsync(key, deviceId.ToString());
    }

    public async Task UpdateDeviceStatusAsync(long deviceId, string status, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var key = RedisKeys.DeviceMeta(deviceId);
        await _redis.HashSetAsync(key, "status", status);
        _logger.LogDebug("Redis Write-Through: updated status for device:meta:{DeviceId} to {Status}",
            deviceId, status);
    }

    public async Task RefreshHeartbeatAsync(long deviceId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var key = RedisKeys.DeviceHeartbeat(deviceId);
        await _redis.StringSetAsync(key, "active", TimeSpan.FromMinutes(10));
        _logger.LogDebug("Redis: refreshed heartbeat for device {DeviceId} (TTL=10min)", deviceId);
    }
}
