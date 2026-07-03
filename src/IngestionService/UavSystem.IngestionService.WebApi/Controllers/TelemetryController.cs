using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using UavSystem.IngestionService.WebApi.Pipeline;
using UavSystem.IngestionService.WebApi.Pipeline.Models;
using UavSystem.Shared.Infrastructure.Caching;

namespace UavSystem.IngestionService.WebApi.Controllers;

[ApiController]
[Route("api/v1/telemetry")]
public sealed class TelemetryController : ControllerBase
{
    private static readonly TimeSpan DeviceCachePositiveTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DeviceCacheNegativeTtl = TimeSpan.FromSeconds(5);
    private static readonly ConcurrentDictionary<long, DeviceCacheEntry> DeviceCache = new();

    private readonly TelemetryIngestionQueue _telemetryQueue;
    private readonly IDatabase _redis;
    private readonly ILogger<TelemetryController> _logger;
    private readonly Channel<LogPacket> _alertChannel;

    public TelemetryController(
        TelemetryIngestionQueue telemetryQueue,
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<TelemetryController> logger,
        Channel<LogPacket> alertChannel)
    {
        _telemetryQueue = telemetryQueue;
        _redis = connectionMultiplexer.GetDatabase();
        _logger = logger;
        _alertChannel = alertChannel;
    }

    [HttpPost("log")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> IngestLog(
        [FromBody] TelemetryPayload payload,
        CancellationToken ct)
    {
        _ = ct;

        if (payload.DeviceId <= 0)
        {
            return BadRequest(new { error = $"Invalid device_id. {payload.DeviceId}" });
        }

        if (!await DeviceExistsAsync(payload.DeviceId))
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
            _alertChannel.Writer.TryWrite(packet);
        }

        if (!_telemetryQueue.TryEnqueue(packet))
        {
            _logger.LogWarning("Telemetry ingestion queue is full; rejecting device {DeviceId}", payload.DeviceId);
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Ingestion queue is full." });
        }

        return Accepted();
    }

    private async Task<bool> DeviceExistsAsync(long deviceId)
    {
        var now = DateTimeOffset.UtcNow;
        if (DeviceCache.TryGetValue(deviceId, out var entry) && entry.ExpiresAt > now)
        {
            return entry.Exists;
        }

        var exists = await _redis.KeyExistsAsync(RedisKeys.DeviceMeta(deviceId));
        DeviceCache[deviceId] = new DeviceCacheEntry(
            exists,
            now.Add(exists ? DeviceCachePositiveTtl : DeviceCacheNegativeTtl));

        return exists;
    }

    private readonly record struct DeviceCacheEntry(bool Exists, DateTimeOffset ExpiresAt);
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
