using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UavSystem.DeviceService.Application.Interfaces;
using UavSystem.DeviceService.Domain.Interfaces;

namespace UavSystem.DeviceService.Infrastructure.BackgroundServices;

public sealed class RedisStartupSyncWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RedisStartupSyncWorker> _logger;

    public RedisStartupSyncWorker(
        IServiceProvider serviceProvider,
        ILogger<RedisStartupSyncWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RedisStartupSyncWorker starting bulk-sync of devices to Redis...");

        try
        {
            await Task.Delay(2000, stoppingToken);

            using var scope = _serviceProvider.CreateScope();
            var deviceRepository = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
            var redisSyncService = scope.ServiceProvider.GetRequiredService<IRedisDeviceSyncService>();

            var devices = await deviceRepository.GetAllAsync(stoppingToken);
            
            int count = 0;
            foreach (var device in devices)
            {
                if (stoppingToken.IsCancellationRequested) break;

                await redisSyncService.SyncDeviceMetadataAsync(
                    device.DeviceId,
                    device.Status.ToString(),
                    device.AssignedMonitorId,
                    device.LocationName,
                    device.ApiKeyHash,
                    stoppingToken);
                    
                if (device.AssignedMonitorId.HasValue)
                {
                    await redisSyncService.SyncMonitorDevicesAsync(
                        device.AssignedMonitorId.Value,
                        device.DeviceId,
                        stoppingToken);
                }
                count++;
            }

            _logger.LogInformation("RedisStartupSyncWorker successfully completed. Synced {Count} devices to Redis.", count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform startup sync of devices to Redis.");
        }
    }
}
