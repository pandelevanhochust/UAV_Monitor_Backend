namespace UavSystem.Shared.Contracts.Events;

/// <summary>
/// Immutable event payload published to RabbitMQ exchange "uav.events"
/// with routing key "device.{DeviceId}.status.changed".
/// Consumed by AlertService on queue "q.status.changes" and broadcast
/// via SignalR to notify supervisors of device state transitions.
/// Triggered by: heartbeat expiry (ONLINE→OFFLINE), edge device self-report,
/// or admin-initiated status override.
/// </summary>
public sealed record DeviceStatusChangedEvent(
    long DeviceId,
    string PreviousStatus,
    string NewStatus,
    DateTime OccurredAt
);
