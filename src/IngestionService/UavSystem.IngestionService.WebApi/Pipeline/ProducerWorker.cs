using System.Text.Json;
using Confluent.Kafka;
using UavSystem.IngestionService.WebApi.Pipeline.Models;

namespace UavSystem.IngestionService.WebApi.Pipeline;

public sealed class Producer : BackgroundService
{
    private const string TopicName = "uav.telemetry.raw";

    private readonly TelemetryIngestionQueue _queue;
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<Producer> _logger;
    private readonly int _workerCount;

    public Producer(
        TelemetryIngestionQueue queue,
        IProducer<string, string> producer,
        ILogger<Producer> logger)
    {
        _queue = queue;
        _producer = producer;
        _logger = logger;
        // Quyet dinh số worker
        _workerCount = ResolveWorkerCount();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // _logger.LogInformation("Kafka telemetry producer started with {WorkerCount} workers", _workerCount);

        // Tạo thành các task chạy loop xử lý song song
        var workers = Enumerable.Range(0, _workerCount)
            .Select(workerId => RunProducerLoopAsync(workerId, stoppingToken));
        var metricsWorker = LogQueueMetricsAsync(stoppingToken);
        // chờ tất cả worker kết thúc
        await Task.WhenAll(workers.Append(metricsWorker));
    }

    private async Task RunProducerLoopAsync(int workerId, CancellationToken stoppingToken)
    {
        // Cơ chế ReadAllAsync giúp giải phóng task ngay lập tức khi Channel trống.
        await foreach (var packet in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                // Đóng gói packet thành JSON string 
                var jsonPacket = JsonSerializer.Serialize(packet);

                // Đẩy vô producer, tách biệt thao tác của Kafka khỏi luồng HTTP
                _producer.Produce(TopicName, new Message<string, string>
                {
                    Key = packet.DeviceId.ToString(),
                    Value = jsonPacket
                }, deliveryReport =>
                {
                    if (deliveryReport.Error.IsError)
                    {
                        _logger.LogError("Kafka delivery failed: {Reason}", deliveryReport.Error.Reason);
                    }
                });
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError(ex, "Kafka producer worker {WorkerId} failed for device {DeviceId}", workerId, packet.DeviceId);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Kafka producer worker {WorkerId} is not available for device {DeviceId}", workerId, packet.DeviceId);
            }
        }
    }


    private static int ResolveWorkerCount()
    {
        return Math.Clamp(Environment.ProcessorCount / 2, 2, 8); //min =2, max =8
    }

    private async Task LogQueueMetricsAsync(CancellationToken stoppingToken)
    {
        var previous = _queue.GetSnapshot();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var current = _queue.GetSnapshot();
            var enqueuedDelta = current.TotalEnqueued - previous.TotalEnqueued;
            var dequeuedDelta = current.TotalDequeued - previous.TotalDequeued;
            var rejectedDelta = current.TotalRejected - previous.TotalRejected;

            _logger.LogInformation(
                "Telemetry queue depth={Depth} enqueued/s={EnqueuedPerSecond:F0} dequeued/s={DequeuedPerSecond:F0} rejected/s={RejectedPerSecond:F0} total_enqueued={TotalEnqueued} total_dequeued={TotalDequeued} total_rejected={TotalRejected}",
                current.Depth,
                enqueuedDelta / 5.0,
                dequeuedDelta / 5.0,
                rejectedDelta / 5.0,
                current.TotalEnqueued,
                current.TotalDequeued,
                current.TotalRejected);

            previous = current;
        }
    }

    public override void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        base.Dispose();
    }
}
