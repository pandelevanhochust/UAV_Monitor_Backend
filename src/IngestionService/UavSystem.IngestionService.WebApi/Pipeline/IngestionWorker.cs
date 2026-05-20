using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using StackExchange.Redis;
using UavSystem.IngestionService.WebApi.Pipeline.Models;
using UavSystem.Shared.Contracts.Events;
using UavSystem.Shared.Contracts.Grpc;
using UavSystem.Shared.Infrastructure.Caching;

namespace UavSystem.IngestionService.WebApi.Pipeline;

/// <summary>
/// Async BackgroundService pulling batches from the Channel&lt;LogPacket&gt;
/// pipeline and executing the 4-step processing sequence:
///
///   Step 1 — Validation / State Delta → gRPC state transition or heartbeat refresh
///   Step 2 — Real-Time Cache → Redis device:latest_log:{id} hash update
///   Step 3 — ClickHouse Write → batch column insertion via ClickHouseColumnWriter
///   Step 4 — Alert Dispatch → RabbitMQ publish if Detected == 1
///
/// FLAT PIPELINE architecture: No call-stack layers (no Clean Architecture
/// overhead). Optimized for throughput — direct Redis/ClickHouse/RabbitMQ access.
/// </summary>
public sealed class IngestionWorker : BackgroundService
{
    private readonly ChannelReader<LogPacket> _channelReader;
    private readonly IConnectionMultiplexer _redis;
    private readonly ClickHouseConnection _clickHouseConnection;
    private readonly InternalDeviceService.InternalDeviceServiceClient _deviceGrpcClient;
    private readonly IConnection _rabbitConnection;
    private readonly IModel _rabbitChannel;
    private readonly ILogger<IngestionWorker> _logger;

    private const string ExchangeName = "uav.events";
    private const int BatchSize = 100;
    private const int BatchTimeoutMs = 500;

    public IngestionWorker(
        ChannelReader<LogPacket> channelReader,
        IConnectionMultiplexer redis,
        ClickHouseConnection clickHouseConnection,
        InternalDeviceService.InternalDeviceServiceClient deviceGrpcClient,
        IConnectionFactory rabbitConnectionFactory,
        ILogger<IngestionWorker> logger)
    {
        _channelReader = channelReader;
        _redis = redis;
        _clickHouseConnection = clickHouseConnection;
        _deviceGrpcClient = deviceGrpcClient;
        _logger = logger;

        // RabbitMQ: durable topic exchange + Publisher Confirms
        _rabbitConnection = rabbitConnectionFactory.CreateConnection();
        _rabbitChannel = _rabbitConnection.CreateModel();
        _rabbitChannel.ExchangeDeclare(ExchangeName, "topic", durable: true, autoDelete: false);
        _rabbitChannel.ConfirmSelect();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IngestionWorker started — pulling from Channel pipeline");

        while (!stoppingToken.IsCancellationRequested)
        {
            var batch = new List<LogPacket>(BatchSize);

            try
            {
                // Collect a batch (up to BatchSize items or BatchTimeoutMs)
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(BatchTimeoutMs);

                try
                {
                    while (batch.Count < BatchSize)
                    {
                        var packet = await _channelReader.ReadAsync(cts.Token);
                        batch.Add(packet);
                    }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Batch timeout elapsed — process what we have
                }

                if (batch.Count == 0)
                    continue;

                _logger.LogDebug("Processing batch of {Count} packets", batch.Count);

                // ── Step 1: Validation / State Delta + Heartbeat ─────────
                await ProcessStateDeltasAsync(batch, stoppingToken);

                // ── Step 2: Real-Time Cache Update ───────────────────────
                await UpdateLatestLogsAsync(batch, stoppingToken);

                // ── Step 3: ClickHouse Batch Write ───────────────────────
                await WriteToClickHouseAsync(batch, stoppingToken);

                // ── Step 4: Alert Dispatch ────────────────────────────────
                await DispatchAlertsAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing ingestion batch of {Count} packets", batch.Count);
                // Continue processing — do not crash the pipeline for transient errors
            }
        }

        _logger.LogInformation("IngestionWorker shutting down");
    }

    /// <summary>
    /// Step 1: For each packet, compare its Status against cached device status.
    /// If changed → call DeviceService gRPC to trigger state transition.
    /// If unchanged → refresh the heartbeat key with 10-minute TTL.
    /// </summary>
    private async Task ProcessStateDeltasAsync(List<LogPacket> batch, CancellationToken ct)
    {
        var db = _redis.GetDatabase();

        foreach (var packet in batch)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var metaKey = RedisKeys.DeviceMeta(packet.DeviceId);
                var cachedStatus = await db.HashGetAsync(metaKey, "status");

                if (!cachedStatus.IsNullOrEmpty &&
                    !string.Equals(cachedStatus.ToString(), packet.Status, StringComparison.OrdinalIgnoreCase))
                {
                    // State delta detected → call DeviceService gRPC for formal transition
                    _logger.LogInformation(
                        "State delta for device {DeviceId}: {Cached} → {Reported}",
                        packet.DeviceId, cachedStatus, packet.Status);

                    var grpcRequest = new UpdateDeviceStatusRequest
                    {
                        DeviceId = packet.DeviceId,
                        NewStatus = packet.Status,
                        Timestamp = Timestamp.FromDateTime(packet.Timestamp.ToUniversalTime())
                    };

                    await _deviceGrpcClient.UpdateDeviceStatusAsync(grpcRequest, cancellationToken: ct);
                }
                else
                {
                    // No state change — refresh heartbeat with 10-minute rolling lease
                    var heartbeatKey = RedisKeys.DeviceHeartbeat(packet.DeviceId);
                    await db.StringSetAsync(heartbeatKey, "active", TimeSpan.FromMinutes(10));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Failed state delta check for device {DeviceId}, continuing batch",
                    packet.DeviceId);
            }
        }
    }

    /// <summary>
    /// Step 2: Update the device:latest_log:{id} Redis hash with the most recent
    /// telemetry data for each device in the batch.
    /// </summary>
    private async Task UpdateLatestLogsAsync(List<LogPacket> batch, CancellationToken ct)
    {
        var db = _redis.GetDatabase();

        // Group by device — only keep the latest timestamp per device in this batch
        var latestPerDevice = batch
            .GroupBy(p => p.DeviceId)
            .Select(g => g.OrderByDescending(p => p.Timestamp).First());

        foreach (var packet in latestPerDevice)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var key = RedisKeys.DeviceLatestLog(packet.DeviceId);
                var entries = new HashEntry[]
                {
                    new("timestamp", packet.Timestamp.ToString("O")),
                    new("detected", packet.Detected.ToString()),
                    new("drone_type", packet.DroneType),
                    new("accuracy", packet.Accuracy.ToString("F4")),
                    new("control_state", packet.ControlState ?? string.Empty)
                };

                await db.HashSetAsync(key, entries);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Failed to update latest_log for device {DeviceId}", packet.DeviceId);
            }
        }
    }

    /// <summary>
    /// Step 3: Batch insert all packets into ClickHouse radar_logs table
    /// using the efficient ClickHouseColumnWriter API (columnar bulk insert).
    /// This avoids row-by-row INSERT overhead and maximizes throughput.
    /// </summary>
    private async Task WriteToClickHouseAsync(List<LogPacket> batch, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            using var bulkCopy = new ClickHouseBulkCopy(_clickHouseConnection)
            {
                DestinationTableName = "radar_logs",
                BatchSize = batch.Count
            };

            var rows = batch.Select(p => new object[]
            {
                p.DeviceId,
                p.Timestamp,
                p.Status,
                p.Detected == 1,  // ClickHouse Bool
                p.DroneType,
                p.Accuracy,
                p.ControlState ?? string.Empty
            });

            await bulkCopy.WriteToServerAsync(rows, ct);

            _logger.LogDebug("ClickHouse: wrote {Count} rows to radar_logs", batch.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "ClickHouse batch write failed for {Count} packets", batch.Count);
            throw; // Propagate to outer catch for retry/alerting
        }
    }

    /// <summary>
    /// Step 4: For any packet where Detected == 1, serialize a DroneDetectedEvent
    /// and publish to RabbitMQ exchange "uav.events" with routing key
    /// "device.{id}.detection.critical" using Publisher Confirms.
    /// </summary>
    private Task DispatchAlertsAsync(List<LogPacket> batch, CancellationToken ct)
    {
        var detections = batch.Where(p => p.Detected == 1).ToList();

        if (detections.Count == 0)
            return Task.CompletedTask;

        foreach (var packet in detections)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var @event = new DroneDetectedEvent(
                    DeviceId: packet.DeviceId,
                    Timestamp: packet.Timestamp,
                    Location: string.Empty,  // Populated from Redis meta at alert service
                    DroneType: packet.DroneType,
                    ControlState: packet.ControlState,
                    Accuracy: packet.Accuracy
                );

                var body = JsonSerializer.SerializeToUtf8Bytes(@event, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var properties = _rabbitChannel.CreateBasicProperties();
                properties.ContentType = "application/json";
                properties.DeliveryMode = 2; // Persistent
                properties.MessageId = Guid.NewGuid().ToString();
                properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                properties.Type = nameof(DroneDetectedEvent);

                var routingKey = $"device.{packet.DeviceId}.detection.critical";

                _rabbitChannel.BasicPublish(
                    exchange: ExchangeName,
                    routingKey: routingKey,
                    mandatory: true,
                    basicProperties: properties,
                    body: body);

                _rabbitChannel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));

                _logger.LogInformation(
                    "Alert dispatched: drone detected by device {DeviceId} (type={DroneType}, accuracy={Accuracy:F2})",
                    packet.DeviceId, packet.DroneType, packet.Accuracy);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Failed to dispatch alert for device {DeviceId}", packet.DeviceId);
            }
        }

        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _rabbitChannel?.Close();
        _rabbitChannel?.Dispose();
        _rabbitConnection?.Close();
        _rabbitConnection?.Dispose();
        _clickHouseConnection?.Dispose();
        base.Dispose();
    }
}
