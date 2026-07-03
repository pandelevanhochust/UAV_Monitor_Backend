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

    public Producer(
        TelemetryIngestionQueue queue,
        IProducer<string, string> producer,
        ILogger<Producer> logger)
    {
        _queue = queue;
        _producer = producer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Kafka telemetry producer started");

        await foreach (var packet in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                var jsonPacket = JsonSerializer.Serialize(packet);
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
                _logger.LogError(ex, "Kafka produce failed for device {DeviceId}", packet.DeviceId);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Kafka producer is not available for device {DeviceId}", packet.DeviceId);
            }
        }
    }

    public override void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        base.Dispose();
    }
}
