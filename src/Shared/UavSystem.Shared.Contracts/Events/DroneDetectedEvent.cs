namespace UavSystem.Shared.Contracts.Events;

/// <summary>
/// Immutable event payload published to RabbitMQ exchange "uav.events"
/// with routing key "device.{DeviceId}.detection.critical".
/// Consumed by AlertService on queue "q.alert.realtime" and broadcast
/// via SignalR to the assigned monitor's WebSocket group.
/// </summary>
public sealed record DroneDetectedEvent(
    long DeviceId,
    DateTime Timestamp,
    string Location,
    string DroneType,
    string? ControlState,
    float Accuracy
);
