using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using StackExchange.Redis;
using UavSystem.AlertService.WebApi.Hubs;
using UavSystem.Shared.Contracts.Events;
using UavSystem.Shared.Infrastructure.Caching;
using UavSystem.Shared.Infrastructure.Messaging;

namespace UavSystem.AlertService.WebApi.Consumers;

/// <summary>
/// RabbitMQ consumer for DeviceStatusChangedEvent messages.
///
/// Queue: q.status.changes
/// Binding: device.*.status.changed → uav.events (topic exchange)
///
/// Same reverse-lookup + SignalR dispatch pattern as DroneAlertConsumer,
/// but fires the "DeviceStatusChanged" SignalR event instead of "DroneDetected".
///
/// Notifies the assigned monitor when their device transitions state
/// (e.g., Online→Offline via heartbeat expiry, Online→Error via edge report).
/// </summary>
public sealed class StatusChangeConsumer : RabbitMqConsumerBase<DeviceStatusChangedEvent>
{
    private readonly IHubContext<AlertHub> _hubContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<StatusChangeConsumer> _logger;

    protected override string QueueName => "q.status.changes";
    protected override string RoutingKey => "device.*.status.changed";

    public StatusChangeConsumer(
        IConnectionFactory connectionFactory,
        IHubContext<AlertHub> hubContext,
        IConnectionMultiplexer redis,
        ILogger<StatusChangeConsumer> logger)
        : base(connectionFactory, logger)
    {
        _hubContext = hubContext;
        _redis = redis;
        _logger = logger;
    }

    protected override async Task HandleMessageAsync(
        DeviceStatusChangedEvent message,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Device status change received: device={DeviceId}, {Previous}→{New}",
            message.DeviceId, message.PreviousStatus, message.NewStatus);

        // ── Redis reverse-lookup: resolve monitor assignment ─────────────
        var db = _redis.GetDatabase();
        var metaKey = RedisKeys.DeviceMeta(message.DeviceId);
// Thay vì gọi 2 lần HashGetAsync, hãy gọi 1 lần HashGetAsync cho mảng các trường
        var fields = await db.HashGetAsync(metaKey, new RedisValue[] { "monitor_id", "location" });
        var monitorId = fields[0];
        var location = fields[1];
        if (monitorId.IsNullOrEmpty || string.IsNullOrWhiteSpace(monitorId.ToString()))
        {
            _logger.LogDebug(
                "No monitor assigned to device {DeviceId} — status change not dispatched",
                message.DeviceId);
            return;
        }

        var userId = monitorId.ToString();

        // ── Enrich with location ─────────────────────────────────────────
        // Thay vì gọi 2 lần HashGetAsync, hãy gọi 1 lần HashGetAsync cho mảng các trường
        var fields = await db.HashGetAsync(metaKey, new RedisValue[] { "monitor_id", "location" });
        var monitorId = fields[0];
        var location = fields[1];
        var payload = new
        {
            message.DeviceId,
            Location = location.IsNullOrEmpty ? "Unknown" : location.ToString(),
            message.PreviousStatus,
            message.NewStatus,
            message.OccurredAt
        };

        // ── SignalR dispatch — ACK only after success ────────────────────
        await _hubContext.Clients.Group(userId)
            .SendAsync("DeviceStatusChanged", payload, cancellationToken);

        _logger.LogInformation(
            "Status change dispatched to monitor '{UserId}': device {DeviceId} {Previous}→{New}",
            userId, message.DeviceId, message.PreviousStatus, message.NewStatus);
    }
}
