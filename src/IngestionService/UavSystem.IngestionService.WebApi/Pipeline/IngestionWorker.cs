using System.Text;
using System.Text.Json;
using Confluent.Kafka;
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
using System.Threading.Channels;

namespace UavSystem.IngestionService.WebApi.Pipeline;

public sealed class IngestionWorker : BackgroundService
{
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
        _logger.LogInformation("[IngestionWorker] Pipeline activated on 16-Core Node - Pulling telemetry batches from Kafka");

        _kafkaConsumer.Subscribe("uav.telemetry.raw");
        var batch = new List<LogPacket>(BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            batch.Clear();

            try
            {
                // Batched consume loop using timeout
                while (batch.Count < BatchSize)
                {
                    try
                    {
                        var consumeResult = _kafkaConsumer.Consume(TimeSpan.FromMilliseconds(BatchTimeoutMs));
                        if (consumeResult == null || consumeResult.IsPartitionEOF)
                        {
                            break; // Timeout reached or EOF
                        }

                        var packet = JsonSerializer.Deserialize<LogPacket>(consumeResult.Message.Value, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (packet != null)
                        {
                            batch.Add(packet);
                        }
                    }
                    catch (ConsumeException e)
                    {
                        if (e.Error.Reason.Contains("Unknown topic", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogWarning("Topic 'uav.telemetry.raw' does not exist yet. Waiting for the first telemetry packet to be produced...");
                            await Task.Delay(2000, stoppingToken);
                            break;
                        }
                        
                        _logger.LogError(e, "Error consuming from Kafka");
                        await Task.Delay(1000, stoppingToken);
                    }
                }

                if (batch.Count == 0)
                    continue;

                _logger.LogDebug("Processing parallelized pipeline batch of {Count} packets", batch.Count);

                // Kích hoạt thực thi đồng thời cả Step 1 và Step 2 để tận dụng tối đa kiến trúc đa nhân của CPU
                var step1Task = ProcessStateDeltasAsync(batch, stoppingToken);
                var step2Task = UpdateLatestLogsAsync(batch, stoppingToken);

                await Task.WhenAll(step1Task, step2Task);

                // Step 3: Ghi dữ liệu khối dạng cột xuống ClickHouse
                await WriteToClickHouseAsync(batch, stoppingToken);

                // Step 4: Commit offsets back to Kafka only after successful ClickHouse write
                _kafkaConsumer.Commit();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical failure inside IngestionWorker batch pipeline execution");
            }
        }
        
        _kafkaConsumer.Close();
    }

    /// <summary>
    /// Step 1 Tối ưu: Chuyển đổi sang gọi gRPC song song song (Fan-Out) 
    /// Thay thế luồng chạy tuần tự cũ để giải phóng nghẽn mạch I/O mạng.
    /// </summary>
    private async Task ProcessStateDeltasAsync(List<LogPacket> batch, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var grpcTasks = new List<Task>(batch.Count);

        // Tạo Batch Redis lệnh để tối ưu hóa truy vấn RTT mạng
        var batchRedis = db.CreateBatch();
        var cacheCheckTasks = batch.Select(packet => new
        {
            Packet = packet,
            MetaKey = RedisKeys.DeviceMeta(packet.DeviceId),
            StatusTask = batchRedis.HashGetAsync(RedisKeys.DeviceMeta(packet.DeviceId), "status")
        }).ToList();

        batchRedis.Execute(); // Đẩy toàn bộ lệnh kiểm tra trạng thái tới Redis cùng 1 lúc

        foreach (var ctx in cacheCheckTasks)
        {
            ct.ThrowIfCancellationRequested();
            var cachedStatus = await ctx.StatusTask;

            if (!cachedStatus.IsNullOrEmpty &&
                !string.Equals(cachedStatus.ToString(), ctx.Packet.Status, StringComparison.OrdinalIgnoreCase))
            {
                // _logger.LogInformation("State delta identified for device {DeviceId}: {Cached} -> {Reported}",
                //     ctx.Packet.DeviceId, cachedStatus, ctx.Packet.Status);

                var grpcRequest = new UpdateDeviceStatusRequest
                {
                    DeviceId = ctx.Packet.DeviceId,
                    NewStatus = ctx.Packet.Status,
                    Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(ctx.Packet.Timestamp.ToUniversalTime())
                };

                // Kích hoạt lệnh gọi gRPC bất đồng bộ, không đứng đợi tuần tự
                grpcTasks.Add(_deviceGrpcClient.UpdateDeviceStatusAsync(grpcRequest, cancellationToken: ct).ResponseAsync);
            }
            else
            {
                var heartbeatKey = RedisKeys.DeviceHeartbeat(ctx.Packet.DeviceId);
                grpcTasks.Add(db.StringSetAsync(heartbeatKey, "active", TimeSpan.FromMinutes(10)));
            }
        }

        if (grpcTasks.Count > 0)
        {
            await Task.WhenAll(grpcTasks); // Đợi toàn bộ các tác vụ gRPC và Heartbeat mạng hoàn thành đồng thời
        }
    }

    /// <summary>
    /// Step 2 Tối ưu: Áp dụng cơ chế Redis Pipelining (CreateBatch).
    /// Gộp toàn bộ lệnh HashSet thành 1 gói dữ liệu mạng duy nhất gửi tới Redis Server.
    /// </summary>
    private Task UpdateLatestLogsAsync(List<LogPacket> batch, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var batchRedis = db.CreateBatch();

        var latestPerDevice = batch
            .GroupBy(p => p.DeviceId)
            .Select(g => g.OrderByDescending(p => p.Timestamp).First());

        foreach (var packet in latestPerDevice)
        {
            if (ct.IsCancellationRequested) break;

            var key = RedisKeys.DeviceLatestLog(packet.DeviceId);
            var entries = new HashEntry[]
            {
                new("timestamp", packet.Timestamp.ToString("O")),
                new("detected", packet.Detected.ToString()),
                new("drone_type", packet.DroneType),
                new("accuracy", packet.Accuracy.ToString("F4")),
                new("control_state", packet.ControlState ?? string.Empty)
            };

            _ = batchRedis.HashSetAsync(key, entries); // Gán lệnh ngầm vào pipeline
        }

        batchRedis.Execute(); // Kích nổ xả toàn bộ dữ liệu khối xuống Redis
        return Task.CompletedTask;
    }

    /// <summary>
    /// Step 3: Lưu khối dữ liệu dạng cột xuống hạ tầng lưu trữ ClickHouse
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
                Convert.ToUInt16(p.DeviceId),
                p.Timestamp,
                p.Status,
                p.Detected == 1,
                p.DroneType,
                p.Accuracy,
                p.ControlState ?? string.Empty,
                p.Latency,
                p.Frequency
            });

            await bulkCopy.InitAsync();
            await bulkCopy.WriteToServerAsync(rows, ct);
            _logger.LogDebug("ClickHouse bulk writer complete: {Count} rows integrated", batch.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Columnar bulk transaction failed for ClickHouse destination target");
            throw;
        }
    }

    public override void Dispose()
    {
        _clickHouseConnection?.Dispose();
        base.Dispose();
    }
}