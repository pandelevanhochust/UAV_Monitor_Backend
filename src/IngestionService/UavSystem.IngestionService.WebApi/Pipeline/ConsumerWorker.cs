using System.Text.Json;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;
using Confluent.Kafka;
using StackExchange.Redis;
using UavSystem.IngestionService.WebApi.Pipeline.Models;
using UavSystem.Shared.Contracts.Grpc;
using UavSystem.Shared.Infrastructure.Caching;

namespace UavSystem.IngestionService.WebApi.Pipeline;

public sealed class IngestionWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IConsumer<string, string> _kafkaConsumer;
    private readonly IConnectionMultiplexer _redis;
    private readonly ClickHouseConnection _clickHouseConnection;
    private readonly InternalDeviceService.InternalDeviceServiceClient _deviceGrpcClient;
    private readonly ILogger<IngestionWorker> _logger;

    private const int BatchSize = 20000;
    private const int BatchTimeoutMs = 1000;

    public IngestionWorker(
        ConsumerConfig consumerConfig,
        IConnectionMultiplexer redis,
        ClickHouseConnection clickHouseConnection,
        InternalDeviceService.InternalDeviceServiceClient deviceGrpcClient,
        ILogger<IngestionWorker> logger)
    {
        _kafkaConsumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        _redis = redis;
        _clickHouseConnection = clickHouseConnection;
        _deviceGrpcClient = deviceGrpcClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IngestionWorker started; consuming Kafka topic uav.telemetry.raw");

        _kafkaConsumer.Subscribe("uav.telemetry.raw");
        var batch = new List<LogPacket>(BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            batch.Clear();

            try
            {
                // Lấy batch data từ kafka
                ConsumeBatch(batch, stoppingToken);

                if (batch.Count == 0)
                {
                    continue;
                }

                _logger.LogDebug("Processing ingestion batch of {Count} packets", batch.Count);

                var stateTask = ProcessStateDeltasAsync(batch, stoppingToken);
                var latestTask = UpdateLatestLogsAsync(batch, stoppingToken);

                await Task.WhenAll(stateTask, latestTask);
                await WriteToClickHouseAsync(batch, stoppingToken);

                _kafkaConsumer.Commit();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical failure inside IngestionWorker batch pipeline");
            }
        }

        _kafkaConsumer.Close();
    }

    // Lấy batch data từ kafka
    private void ConsumeBatch(List<LogPacket> batch, CancellationToken ct)
    {
        while (batch.Count < BatchSize && !ct.IsCancellationRequested)
        {
            try
            {
                var consumeResult = _kafkaConsumer.Consume(TimeSpan.FromMilliseconds(BatchTimeoutMs));
                if (consumeResult == null || consumeResult.IsPartitionEOF)
                {
                    break;
                }

                var packet = JsonSerializer.Deserialize<LogPacket>(
                    consumeResult.Message.Value,
                    JsonOptions);
                batch.Add(packet);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping malformed telemetry message from Kafka");
            }
            catch (ConsumeException ex)
            {
                if (ex.Error.Reason.Contains("Unknown topic", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Kafka topic uav.telemetry.raw does not exist yet");
                    Thread.Sleep(2000);
                    break;
                }

                _logger.LogError(ex, "Error consuming from Kafka");
                Thread.Sleep(1000);
            }
        }
    }

    // Lấy ra state cuối cùng trong batch data để xử lý
    private async Task ProcessStateDeltasAsync(List<LogPacket> batch, CancellationToken ct)
    {
        var latestByDevice = GetLatestPacketByDevice(batch);
        if (latestByDevice.Count == 0)
        {
            return;
        }

        var db = _redis.GetDatabase();
        var statusBatch = db.CreateBatch();
        var statusChecks = latestByDevice.Values
            .Select(packet => new
            {
                Packet = packet,
                StatusTask = statusBatch.HashGetAsync(RedisKeys.DeviceMeta(packet.DeviceId), "status")
            })
            .ToArray();

        statusBatch.Execute();

        var grpcTasks = new List<Task>(statusChecks.Length);
        var heartbeatBatch = db.CreateBatch();
        var heartbeatTasks = new List<Task>(statusChecks.Length);

        foreach (var check in statusChecks)
        {
            ct.ThrowIfCancellationRequested();
            var cachedStatus = await check.StatusTask;

            if (!cachedStatus.IsNullOrEmpty &&
                !string.Equals(cachedStatus.ToString(), check.Packet.Status, StringComparison.OrdinalIgnoreCase))
            {
                var request = new UpdateDeviceStatusRequest
                {
                    DeviceId = check.Packet.DeviceId,
                    NewStatus = check.Packet.Status,
                    Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(
                        check.Packet.Timestamp.ToUniversalTime())
                };

                grpcTasks.Add(_deviceGrpcClient.UpdateDeviceStatusAsync(request, cancellationToken: ct).ResponseAsync);
            }
            else
            {
                heartbeatTasks.Add(heartbeatBatch.StringSetAsync(
                    RedisKeys.DeviceHeartbeat(check.Packet.DeviceId),
                    "active",
                    TimeSpan.FromMinutes(10)));
            }
        }

        heartbeatBatch.Execute();

        if (grpcTasks.Count > 0 || heartbeatTasks.Count > 0)
        {
            await Task.WhenAll(grpcTasks.Concat(heartbeatTasks));
        }
    }

    // Lấy log cuối cùng để update vào Redis
    private async Task UpdateLatestLogsAsync(List<LogPacket> batch, CancellationToken ct)
    {
        var latestByDevice = GetLatestPacketByDevice(batch);
        if (latestByDevice.Count == 0)
        {
            return;
        }

        var db = _redis.GetDatabase();
        var redisBatch = db.CreateBatch();
        var tasks = new List<Task>(latestByDevice.Count);

        foreach (var packet in latestByDevice.Values)
        {
            ct.ThrowIfCancellationRequested();

            var entries = new HashEntry[]
            {
                new("timestamp", packet.Timestamp.ToString("O")),
                new("detected", packet.Detected.ToString()),
                new("drone_type", packet.DroneType),
                new("accuracy", packet.Accuracy.ToString("F4")),
                new("control_state", packet.ControlState ?? string.Empty)
            };

            tasks.Add(redisBatch.HashSetAsync(RedisKeys.DeviceLatestLog(packet.DeviceId), entries));
        }

        redisBatch.Execute();
        await Task.WhenAll(tasks);
    }

    // Bulk Insert
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

            var rows = batch.Select(packet => new object[]
            {
                Convert.ToUInt16(packet.DeviceId),
                packet.Timestamp,
                packet.Status,
                packet.Detected == 1,
                packet.DroneType,
                packet.Accuracy,
                packet.ControlState ?? string.Empty,
                packet.Latency,
                packet.Frequency
            });

            await bulkCopy.InitAsync();
            await bulkCopy.WriteToServerAsync(rows, ct);
            _logger.LogDebug("ClickHouse bulk write complete: {Count} rows", batch.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClickHouse bulk write failed");
            throw;
        }
    }

    private static Dictionary<long, LogPacket> GetLatestPacketByDevice(List<LogPacket> batch)
    {
        var latestByDevice = new Dictionary<long, LogPacket>();

        foreach (var packet in batch)
        {
            if (!latestByDevice.TryGetValue(packet.DeviceId, out var current) ||
                packet.Timestamp > current.Timestamp)
            {
                latestByDevice[packet.DeviceId] = packet;
            }
        }

        return latestByDevice;
    }

    public override void Dispose()
    {
        _clickHouseConnection.Dispose();
        base.Dispose();
    }
}
