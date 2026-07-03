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
/// RabbitMQ consumer for DroneDetectedEvent messages.
///
/// Queue: q.alert.realtime
/// Binding: device.*.detection.critical → uav.events (topic exchange)
///
/// Processing flow:
///   1. Deserialize DroneDetectedEvent from the message body.
///   2. Perform Redis reverse-lookup: read device:meta:{device_id} → "monitor_id"
///      to resolve which userId owns this device.
///   3. Inject IHubContext&lt;AlertHub&gt; and fire the serialized event to
///      that user's SignalR group: Clients.Group(userId).SendAsync("DroneDetected", ...).
///   4. BasicAck ONLY after successful SignalR dispatch (Appendix C Rule 9).
///      On failure: log via Serilog, BasicNack(requeue: true).
///
/// Inherits from RabbitMqConsumerBase&lt;DroneDetectedEvent&gt; which handles:
///   - Queue declaration with DLX routing to q.malformed.dlq
///   - Manual ACK (autoAck: false)
///   - JSON deserialization
///   - Error handling with BasicNack on exception
/// </summary>
public sealed class DroneAlertConsumer : RabbitMqConsumerBase<DroneDetectedEvent>
{
    private readonly IHubContext<AlertHub> _hubContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<DroneAlertConsumer> _logger;

    protected override string QueueName => "q.alert.realtime";
    protected override string RoutingKey => "device.*.detection.critical";

    public DroneAlertConsumer(
        IConnectionFactory connectionFactory,
        IHubContext<AlertHub> hubContext,
        IConnectionMultiplexer redis,
        ILogger<DroneAlertConsumer> logger)
        : base(connectionFactory, logger)
    {
        _hubContext = hubContext;
        _redis = redis;
        _logger = logger;
    }

    protected override async Task HandleMessageAsync(
        DroneDetectedEvent message,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Drone detected alert received: device={DeviceId}, type={DroneType}, accuracy={Accuracy:F2}",
            message.DeviceId, message.DroneType, message.Accuracy);

        // ── Step 1: Redis reverse-lookup — resolve which monitor owns this device ──
        var db = _redis.GetDatabase();
        var metaKey = RedisKeys.DeviceMeta(message.DeviceId);
        // Thay vì gọi 2 lần HashGetAsync, hãy gọi 1 lần HashGetAsync cho mảng các trường
        var fields = await db.HashGetAsync(metaKey, new RedisValue[] { "monitor_id", "location" });
        var monitorId = fields[0];
        var location = fields[1];
        if (monitorId.IsNullOrEmpty || string.IsNullOrWhiteSpace(monitorId.ToString()))
        {
            _logger.LogWarning(
                "No monitor assigned to device {DeviceId} — alert will not be dispatched to any user",
                message.DeviceId);
            // Still ACK — message is valid but no one to notify
            return;
        }

        var userId = monitorId.ToString();

        // ── Step 2: Enrich with location from Redis metadata ────────────────
        var enrichedPayload = new
        {
            message.DeviceId,
            message.Timestamp,
            Location = location.IsNullOrEmpty ? "Unknown" : location.ToString(),
            message.DroneType,
            message.ControlState,
            message.Accuracy
        };

        // ── Step 3: SignalR dispatch to the specific monitor's group ─────────
        // This is the critical path — BasicAck happens AFTER this succeeds.
        // If this throws, the base class catches it and calls BasicNack(requeue: true).
        await _hubContext.Clients.Group(userId)
            .SendAsync("DroneDetected", enrichedPayload, cancellationToken);

        _logger.LogInformation(
            "Alert dispatched to monitor '{UserId}' for device {DeviceId} (drone: {DroneType})",
            userId, message.DeviceId, message.DroneType);
    }
}
