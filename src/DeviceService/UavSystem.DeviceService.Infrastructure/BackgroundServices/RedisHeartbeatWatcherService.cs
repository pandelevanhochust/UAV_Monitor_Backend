using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using UavSystem.DeviceService.Application.Features.Devices.Commands;
using UavSystem.DeviceService.Infrastructure.Messaging;

namespace UavSystem.DeviceService.Infrastructure.BackgroundServices;

/// <summary>
/// Subscribes to Redis keyspace notifications (__keyevent@0__:expired).
/// When a device:heartbeat:{device_id} key expires (TTL=10min with no refresh),
/// this triggers a device OFFLINE transition via MediatR command,
/// followed by a RabbitMQ status change publication.
/// </summary>
public sealed class RedisHeartbeatWatcherService : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DeviceStatusPublisher _statusPublisher;
    private readonly ILogger<RedisHeartbeatWatcherService> _logger;

    private const string HeartbeatKeyPrefix = "device:heartbeat:";

    public RedisHeartbeatWatcherService(
        IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory,
        DeviceStatusPublisher statusPublisher,
        ILogger<RedisHeartbeatWatcherService> logger)
    {
        _redis = redis;
        _scopeFactory = scopeFactory;
        _statusPublisher = statusPublisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = _redis.GetSubscriber();

        // Subscribe to key expiration events on database 0
        await subscriber.SubscribeAsync(
            new RedisChannel("__keyevent@0__:expired", RedisChannel.PatternMode.Literal),
            async (_, expiredKey) =>
            {
                var key = expiredKey.ToString();

                // Only react to heartbeat key expirations
                if (!key.StartsWith(HeartbeatKeyPrefix))
                    return;

                var deviceIdStr = key[HeartbeatKeyPrefix.Length..];
                if (!long.TryParse(deviceIdStr, out var deviceId))
                {
                    _logger.LogWarning("Failed to parse device ID from expired key: {Key}", key);
                    return;
                }

                _logger.LogInformation(
                    "Heartbeat expired for device {DeviceId} — transitioning to OFFLINE", deviceId);

                try
                {
                    // Use a scoped MediatR sender to update PostgreSQL + Redis
                    using var scope = _scopeFactory.CreateScope();
                    var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                    var success = await sender.Send(
                        new UpdateDeviceStatusCommand(deviceId, "Offline"),
                        stoppingToken);

                    if (success)
                    {
                        // Publish status change event to RabbitMQ
                        await _statusPublisher.PublishStatusChangedAsync(
                            deviceId, "Online", "Offline", stoppingToken);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex,
                        "Error processing heartbeat expiry for device {DeviceId}", deviceId);
                }
            });

        _logger.LogInformation("Redis heartbeat watcher started — listening for key expirations");

        // Keep alive until cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Redis heartbeat watcher shutting down");
        }
    }
}
