namespace UavSystem.IngestionService.WebApi.Pipeline.Models;

public readonly record struct LogPacket
{
    public required long DeviceId { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Status { get; init; }      // "Online" | "Offline" | "Error"
    public required int Detected { get; init; }         // 1 = drone detected, 0 = clear
    public required string DroneType { get; init; }     // e.g., "DJI Mavic 3", "Unknown"
    public required float Accuracy { get; init; }       // 0.0–1.0 confidence score
    public string? ControlState { get; init; }          // "Controlled" | "Autonomous" | null
    public required float Latency { get; init; }          // Latency in ms
    public required float Frequency { get; init; }        // Signal frequency in MHz
}
