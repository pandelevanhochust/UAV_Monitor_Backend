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
using System.Threading.Tasks;
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
        _kafkaConsumer.Subscribe("uav.telemetry.raw");
        var batch = new List<LogPacket>(BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            batch.Clear();

            try
            {
                while (batch.Count < BatchSize)
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
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        
                        if (packet != null)
                        {
                            batch.Add(packet);
                        }
                    }
                    catch (ConsumeException e)
                    {
                        if (e.Error.Reason.Contains("Unknown topic", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogWarning("Topic 'uav.telemetry.raw' chưa tồn tại. Đang đợi gói tin đầu tiên...");
                            await Task.Delay(2000, stoppingToken);
                            break;
                        }
                        
                        _logger.LogError(e, "Lỗi kéo tin từ Kafka");
                        await Task.Delay(1000, stoppingToken);
                    }
                }

                if (batch.Count == 0)
                    continue;

                _logger.LogDebug("Đang xử lý song song mẻ đường ống gồm {Count} gói tin", batch.Count);

                var step1Task = ProcessStateDeltasAsync(batch, stoppingToken)
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            _logger.LogError(t.Exception!.InnerException, "[Step1] ProcessStateDeltas thất bại — bỏ qua, tiếp tục pipeline");
                    }, TaskContinuationOptions.ExecuteSynchronously);

                var step2Task = UpdateLatestLogsAsync(batch, stoppingToken)
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            _logger.LogError(t.Exception!.InnerException, "[Step2] UpdateLatestLogs thất bại — bỏ qua, tiếp tục pipeline");
                    }, TaskContinuationOptions.ExecuteSynchronously);

                await Task.WhenAll(step1Task, step2Task);

                await WriteToClickHouseAsync(batch, stoppingToken);

                _kafkaConsumer.Commit();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Thất bại nghiêm trọng trong vòng lặp tiêu hóa dữ liệu của IngestionWorker");
            }
        }
        
        _kafkaConsumer.Close();
    }

    private async Task ProcessStateDeltasAsync(List<LogPacket> batch, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var grpcTasks = new List<Task>();

        var latestPerDevice = batch
            .GroupBy(p => p.DeviceId)
            .Select(g => g.OrderByDescending(p => p.Timestamp).First())
            .ToList();

        var checkBatch = db.CreateBatch();
        var cacheCheckTasks = latestPerDevice.Select(packet => new
        {
            Packet = packet,
            StatusTask = checkBatch.HashGetAsync(RedisKeys.DeviceMeta(packet.DeviceId), "status")
        }).ToList();

        checkBatch.Execute();

        var heartbeatBatch = db.CreateBatch();

        foreach (var ctx in cacheCheckTasks)
        {
            ct.ThrowIfCancellationRequested();
            
            var cachedStatus = await ctx.StatusTask; 

            if (!cachedStatus.IsNullOrEmpty &&
                !string.Equals(cachedStatus.ToString(), ctx.Packet.Status, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Phát hiện thay đổi trạng thái của thiết bị {DeviceId}: {Cached} -> {Reported}",
                    ctx.Packet.DeviceId, cachedStatus, ctx.Packet.Status);

                var grpcRequest = new UpdateDeviceStatusRequest
                {
                    DeviceId = ctx.Packet.DeviceId,
                    NewStatus = ctx.Packet.Status,
                    Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(ctx.Packet.Timestamp.ToUniversalTime())
                };

                grpcTasks.Add(_deviceGrpcClient.UpdateDeviceStatusAsync(grpcRequest, cancellationToken: ct).ResponseAsync);
            }
            else
            {
                var heartbeatKey = RedisKeys.DeviceHeartbeat(ctx.Packet.DeviceId);
                _ = heartbeatBatch.HashSetAsync(heartbeatKey, new HashEntry[] { new("state", "active") });
                _ = heartbeatBatch.KeyExpireAsync(heartbeatKey, TimeSpan.FromMinutes(10));
            }
        }

        heartbeatBatch.Execute();

        if (grpcTasks.Count > 0)
        {
            await Task.WhenAll(grpcTasks); 
        }
    }

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

            _ = batchRedis.HashSetAsync(key, entries);
        }

        batchRedis.Execute(); 
        return Task.CompletedTask;
    }


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
            _logger.LogDebug("Hoàn thành bulk write ClickHouse: tích hợp thành công {Count} dòng", batch.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Thảm họa ghi dữ liệu cột Bulk Insert xuống ClickHouse thất bại");
            throw;
        }
    }

    public override void Dispose()
    {
        _clickHouseConnection?.Dispose();
        base.Dispose();
    }
}