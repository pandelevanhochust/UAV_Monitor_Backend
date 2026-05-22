using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using UavSystem.IngestionService.WebApi.Pipeline.Models;
using UavSystem.Shared.Infrastructure.Caching;

namespace UavSystem.IngestionService.WebApi.Controllers;

[ApiController]
[Route("api/v1/telemetry")]
public sealed class TelemetryController : ControllerBase
{
    private readonly ChannelWriter<LogPacket> _channelWriter;
    private readonly IDatabase _redis;
    private readonly ILogger<TelemetryController> _logger;

    public TelemetryController(
        ChannelWriter<LogPacket> channelWriter,
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<TelemetryController> logger)
    {
        _channelWriter = channelWriter;
        _redis = connectionMultiplexer.GetDatabase();
        _logger = logger;
    }

    /// <summary>
    /// POST /api/v1/telemetry/log — High-velocity ingestion endpoint.
    /// Validates device API key against Redis cache, then pushes to
    /// the in-process Channel pipeline. Returns 202 Accepted immediately
    /// (fire-and-forward, NOT fire-and-forget — the pipeline guarantees
    /// processing via the IngestionWorker BackgroundService).
    /// </summary>
    [HttpPost("log")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> IngestLog(
        [FromBody] TelemetryPayload payload,
        [FromHeader(Name = "X-Device-API-Key")] string? apiKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Unauthorized(new { error = "Missing X-Device-API-Key header." });
        }

        if (payload.DeviceId <= 0)
        {
            return BadRequest(new { error = $"Invalid device_id. {payload.DeviceId}" });
        }

        // ── API Key Verification via Redis Hash ──────────────────────────
        // Read the stored BCrypt hash from device:meta:{id} → api_key_hash
        var metaKey = RedisKeys.DeviceMeta(payload.DeviceId);
        var storedHash = await _redis.HashGetAsync(metaKey, "api_key_hash");

        if (storedHash.IsNullOrEmpty)
        {
            _logger.LogWarning("Device {DeviceId} not found in Redis cache", payload.DeviceId);
            return Unauthorized(new { error = "Device not registered." });
        }

        if (!BCrypt.Net.BCrypt.Verify(apiKey, storedHash.ToString()))
        {
            _logger.LogWarning("Invalid API key for device {DeviceId}", payload.DeviceId);
            return Unauthorized(new { error = "Invalid API key." });
        }

        // ── Map to LogPacket and push to Channel pipeline ────────────────
        var packet = new LogPacket
        {
            DeviceId = payload.DeviceId,
            Timestamp = payload.Timestamp,
            Status = payload.Status,
            Detected = payload.Detected,
            DroneType = payload.DroneType,
            Accuracy = payload.Accuracy,
            ControlState = payload.ControlState,
            Latency = payload.Latency
        };

        if (!_channelWriter.TryWrite(packet))
        {
            // Channel is full — backpressure scenario
            _logger.LogWarning("Channel backpressure: dropped packet from device {DeviceId}", payload.DeviceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Ingestion pipeline is at capacity. Retry later." });
        }

        return Accepted(new { device_id = payload.DeviceId, status = "queued" });
    }
}

/// <summary>
/// JSON-serializable request body for telemetry ingestion.
/// Uses System.Text.Json naming conventions (camelCase).
/// </summary>
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
}
