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
        
        // Ghi chú: Kết nối RabbitMQ đã được gỡ bỏ hoàn toàn khỏi Worker 
        // do Fast Path đã chuyển ra ngoài TelemetryController ✓
    }

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    _logger.LogInformation("[IngestionWorker] 🚀 HIGH-THROUGHPUT PIPELINE ACTIVATED ON 16-CORE NODE 🚀");
    _kafkaConsumer.Subscribe("uav.telemetry.raw");
    
    // BƯỚC 1: Tạo một khay chứa RAM nội bộ siêu tốc (Không giới hạn hoặc Bounded lớn)
    // Đây là cái bể giảm chấn để luồng kéo tin Kafka không bị block bởi luồng ghi đĩa ClickHouse
    var internalBuffer = Channel.CreateBounded<LogPacket>(new BoundedChannelOptions(150000) {
        FullMode = BoundedChannelFullMode.Wait,
        SingleWriter = true, // Chỉ có 1 luồng Kafka Consume ghi vào
        SingleReader = false  
    });

    // BƯỚC 2: LUỒNG ĐỘC QUYỀN KÉO TIN (Chạy trên 1 thớt CPU riêng biệt)
    // Nhiệm vụ duy nhất: Kéo tin kịch tốc độ từ Kafka, ném vào RAM Channel rồi quay lại kéo tiếp, KHÔNG ĐỢI AI.
    var kafkaPullerTask = Task.Run(async () => {
        try {
            while (!stoppingToken.IsCancellationRequested) {
                // Đặt timeout cực nhỏ (5-10ms) để luồng này giật tin liên tục không ngừng nghỉ
                var consumeResult = _kafkaConsumer.Consume(TimeSpan.FromMilliseconds(10));
                if (consumeResult != null) {
                    var packet = JsonSerializer.Deserialize<LogPacket>(consumeResult.Message.Value, 
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (packet != null) {
                        await internalBuffer.Writer.WriteAsync(packet, stoppingToken);
                    }
                }
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Kafka Puller crashed"); }
        finally { internalBuffer.Writer.Complete(); }
    }, stoppingToken);

    // BƯỚC 3: CỤM 4 WORKERS SONG SONG (Tận dụng triệt để 16-Core Node)
    // Đẻ ra 4 Tasks chạy song song hoàn toàn độc lập để chia nhau dọn rác trong RAM Channel
    int parallelWorkerCount = 4;
    var processingTasks = Enumerable.Range(0, parallelWorkerCount).Select(async workerId => {
        _logger.LogInformation($" ↳ [Worker Thread {workerId}] Initialized and listening to RAM Channel.");
        var batch = new List<LogPacket>(BatchSize);

        while (await internalBuffer.Reader.WaitToReadAsync(stoppingToken)) {
            batch.Clear();
            
            // Gom đủ 20,000 dòng hoặc hốt sạch những gì đang có trên RAM hiện tại
            while (batch.Count < BatchSize && internalBuffer.Reader.TryRead(out var packet)) {
                batch.Add(packet);
            }

            if (batch.Any()) {
                try {
                    // Xử lý song song logic nghiệp vụ nội bộ
                    var step1 = ProcessStateDeltasAsync(batch, stoppingToken);
                    var step2 = UpdateLatestLogsAsync(batch, stoppingToken);
                    await Task.WhenAll(step1, step2);

                    // Xả dữ liệu dạng cột xuống ClickHouse
                    // 4 luồng này sẽ thay nhau nã Bulk Insert, ClickHouse sẽ nuốt trọn vẹn
                    await WriteToClickHouseAsync(batch, stoppingToken);
                }
                catch (Exception ex) {
                    _logger.LogError(ex, $"Critical failure in Worker Thread {workerId}");
                }
            }
        }
    }).ToArray();

    // Khởi chạy toàn bộ guồng máy
    await Task.WhenAll(kafkaPullerTask, Task.WhenAll(processingTasks));
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
                _logger.LogInformation("State delta identified for device {DeviceId}: {Cached} -> {Reported}",
                    ctx.Packet.DeviceId, cachedStatus, ctx.Packet.Status);

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