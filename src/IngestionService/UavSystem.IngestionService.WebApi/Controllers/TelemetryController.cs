using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;
using System.Threading.Channels; // 💡 THÊM DÒNG NÀY VÀO ĐẦU FILE TELEMETRYCONTROLLER.CS
using StackExchange.Redis;
using UavSystem.IngestionService.WebApi.Pipeline.Models;
using UavSystem.Shared.Contracts.Events;
using UavSystem.Shared.Infrastructure.Caching;

namespace UavSystem.IngestionService.WebApi.Controllers;

[ApiController]
[Route("api/v1/telemetry")]
public sealed class TelemetryController : ControllerBase, IDisposable
{
    private readonly IProducer<string, string> _kafkaProducer;
    private readonly IDatabase _redis;
    private readonly ILogger<TelemetryController> _logger;

    // ── Dedicated RabbitMQ connection for immediate alert dispatch ────────────
    // Intentionally separate from the IngestionWorker's connection so that
    // alert publishing is never blocked by the batch pipeline.
    // private readonly IConnection _rabbitConnection;
    // private readonly IModel _rabbitChannel;

    // IModel (RabbitMQ channel) is NOT thread-safe. Multiple concurrent HTTP threads
    // calling BasicPublish on the same channel causes corruption under high RPS.
    // SemaphoreSlim(1,1) serializes access without blocking the thread pool (async-aware).
    private static readonly SemaphoreSlim _publishLock = new(1, 1);

    private const string ExchangeName = "uav.events";
    private readonly Channel<LogPacket> _alertChannel;

    public TelemetryController(
        IProducer<string, string> kafkaProducer,
        IConnectionMultiplexer connectionMultiplexer,
        IConnectionFactory rabbitConnectionFactory,
        ILogger<TelemetryController> logger,
        Channel<LogPacket> alertChannel)
    {
        _kafkaProducer = kafkaProducer;
        _redis = connectionMultiplexer.GetDatabase();
        _logger = logger;
        _alertChannel = alertChannel;
        // _rabbitConnection = ConnectWithRetry(rabbitConnectionFactory);
        // _rabbitChannel = _rabbitConnection.CreateModel();
        // _rabbitChannel.ExchangeDeclare(ExchangeName, "topic", durable: true, autoDelete: false);
    }

    private static IConnection ConnectWithRetry(IConnectionFactory factory, int maxAttempts = 5)
    {
        Exception? lastEx = null;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return factory.CreateConnection();
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                lastEx = ex;
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // 2s, 4s, 8s, 16s
                Thread.Sleep(delay);
            }
        }
        throw new InvalidOperationException(
            $"[TelemetryController] Failed to connect to RabbitMQ after {maxAttempts} attempts.", lastEx);
    }

    /// <summary>
    /// POST /api/v1/telemetry/log — High-velocity ingestion endpoint.
    /// Validates device API key against Redis cache, then:
    ///   1. If Detected == 1: immediately publishes DroneDetectedEvent to RabbitMQ
    ///      (bypasses the batch wait — WebSocket alert fires in ~10-20ms).
    ///   2. Always: pushes to the in-process Channel pipeline for ClickHouse batch write.
    /// Returns 202 Accepted immediately.
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


        // ── Map to LogPacket and push to Channel pipeline ────────────────────
        // The IngestionWorker handles: Redis cache update + ClickHouse batch write.
        // Alert dispatch is handled above and skipped inside IngestionWorker.
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
        // Ghi thẳng vào RAM .NET Channel, mất chưa tới 1 micro-giây, giải phóng HTTP ngay lập tức!
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

    _logger.LogInformation("Telemetry log queued for device {DeviceId} at {Timestamp}", payload.DeviceId, payload.Timestamp);

// Trả về 202 ngay lập tức tại đây, không tốn thời gian chờ đợi mạng!
return Accepted(new { device_id = payload.DeviceId, status = "queued" });
    }

    public void Dispose()
    {
        // _rabbitChannel?.Close();
        // _rabbitChannel?.Dispose();
        // _rabbitConnection?.Close();
        // _rabbitConnection?.Dispose();
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
    public float Frequency { get; init; }
}


