using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using UavSystem.Shared.Contracts.Events;
using UavSystem.Shared.Infrastructure.Messaging;

namespace UavSystem.DeviceService.Infrastructure.Messaging;

/// <summary>
/// Publishes device status change events to RabbitMQ exchange "uav.events"
/// with routing key "device.{deviceId}.status.changed".
/// Uses raw RabbitMQ.Client with Publisher Confirms (Appendix C Rule 8).
/// MassTransit is STRICTLY FORBIDDEN.
/// </summary>
public sealed class DeviceStatusPublisher : RabbitMqPublisherBase
{
    public DeviceStatusPublisher(IConnectionFactory connectionFactory, ILogger<DeviceStatusPublisher> logger)
        : base(connectionFactory, logger)
    {
    }

    public Task PublishStatusChangedAsync(
        long deviceId, string previousStatus, string newStatus,
        CancellationToken cancellationToken = default)
    {
        var @event = new DeviceStatusChangedEvent(
            DeviceId: deviceId,
            PreviousStatus: previousStatus,
            NewStatus: newStatus,
            OccurredAt: DateTime.UtcNow
        );

        var routingKey = $"device.{deviceId}.status.changed";
        return PublishAsync(routingKey, @event, cancellationToken);
    }
}
