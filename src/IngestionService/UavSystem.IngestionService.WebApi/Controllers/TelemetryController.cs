using System.Text.Json;
using Confluent.Kafka;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Channels;
using StackExchange.Redis;
using UavSystem.IngestionService.WebApi.Pipeline.Models;
using UavSystem.Shared.Infrastructure.Caching;

namespace UavSystem.IngestionService.WebApi.Controllers;

[ApiController]
[Route("api/v1/telemetry")]
public sealed class TelemetryController : ControllerBase
{
    private readonly IProducer<string, string> _kafkaProducer;
    private readonly IDatabase _redis;
    private readonly ILogger<TelemetryController> _logger;
    private readonly Channel<LogPacket> _alertChannel;

    public TelemetryController(
        IProducer<string, string> kafkaProducer,
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<TelemetryController> logger,
        Channel<LogPacket> alertChannel)
    {
        _kafkaProducer = kafkaProducer;
        _redis = connectionMultiplexer.GetDatabase();
        _logger = logger;
        _alertChannel = alertChannel;
    }

    [HttpPost("log")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> IngestLog(
        [FromBody] TelemetryPayload payload,
        [FromHeader(Name = "X-Device-API-Key")] string? apiKey,
        CancellationToken ct)
    {
        if (payload.DeviceId <= 0)
        {
            return BadRequest(new { error = $"Invalid device_id. {payload.DeviceId}" });
        }

        var metaKey = RedisKeys.DeviceMeta(payload.DeviceId);
        bool deviceExists = await _redis.KeyExistsAsync(metaKey);

        if (!deviceExists)
        {
            _logger.LogWarning("Device {DeviceId} not found in Redis cache", payload.DeviceId);
            return Unauthorized(new { error = "Device not registered." });
        }


        var packet = new LogPacket
        {
            DeviceId = payload.DeviceId,
            Timestamp = payload.Timestamp,
            Status = payload.Status,
            Detected = payload.Detected,
            DroneType = payload.DroneType,
            Accuracy = payload.Accuracy,
            ControlState = payload.ControlState,
            Latency = payload.Latency,
            Frequency = payload.Frequency
        };

            if (payload.Detected == 1)
    {
        // Ghi thẳng Alert vào Channel
        _alertChannel.Writer.TryWrite(packet);
        
    }


        var jsonPacket = JsonSerializer.Serialize(packet);



    _kafkaProducer.Produce("uav.telemetry.raw", new Message<string, string>
    {
        Key = payload.DeviceId.ToString(),
        Value = jsonPacket
    }, deliveryReport => 
    {
        if (deliveryReport.Error.IsError)
        {
            _logger.LogError("Kafka delivery failed: {Reason}", deliveryReport.Error.Reason);
        }
});

    // _logger.LogInformation("Telemetry log queued for device {DeviceId} at {Timestamp}", payload.DeviceId, payload.Timestamp);

        return Accepted();
    }
}

public sealed record TelemetryPayload
{
    public long DeviceId { get; init; }
    public DateTime Timestamp { get; init; }
    public string Status { get; init; } = "Online";
    public int Detected { get; init; }
    public string DroneType { get; init; } = "Unknown";
    public float Accuracy { get; init; }
    public string? ControlState { get; init; }
    public float Latency { get; init; }
    public float Frequency { get; init; }
}


