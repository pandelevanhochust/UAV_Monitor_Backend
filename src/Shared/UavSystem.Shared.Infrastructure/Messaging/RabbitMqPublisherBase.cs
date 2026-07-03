using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace UavSystem.Shared.Infrastructure.Messaging;

/// <summary>
/// Abstract base class for RabbitMQ publishers using raw RabbitMQ.Client.
/// Enforces:
///   - Topic exchange "uav.events" (durable) declaration
///   - Publisher Confirms (ConfirmSelect + WaitForConfirmsOrDie) per Appendix C Rule 8
///   - Persistent delivery mode (delivery_mode: 2)
///   - JSON serialization of event records
///
/// MassTransit is STRICTLY FORBIDDEN — all messaging uses raw RabbitMQ.Client.
/// </summary>
public abstract class RabbitMqPublisherBase : IDisposable
{
    private const string ExchangeName = "uav.events";
    private const string ExchangeType = "topic";

    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ILogger _logger;
    private bool _disposed;

    protected RabbitMqPublisherBase(IConnectionFactory connectionFactory, ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));;

        // ── Retry connection with exponential backoff ─────────────────────────
        // RabbitMQ may still be initializing when services start. Without retries,
        // the constructor throws and the entire service crashes on the first boot.
        // Max wait: 2+4+8+16+32 = 62 seconds total before giving up.
        const int maxAttempts = 5;
        IConnection? connection = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                connection = connectionFactory.CreateConnection();
                break; // Success — exit retry loop
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // 2s, 4s, 8s, 16s
                _logger.LogWarning(
                    "RabbitMQ connection attempt {Attempt}/{Max} failed: {Message}. Retrying in {Delay}s...",
                    attempt, maxAttempts, ex.Message, delay.TotalSeconds);
                Thread.Sleep(delay);
            }
        }

        _connection = connection ?? throw new InvalidOperationException(
            $"Failed to connect to RabbitMQ after {maxAttempts} attempts.");

        _channel = _connection.CreateModel();

        // Declare durable topic exchange
        _channel.ExchangeDeclare(
            exchange: ExchangeName,
            type: ExchangeType,
            durable: true,
            autoDelete: false,
            arguments: null);

        // Enable Publisher Confirms — never fire-and-forget (Appendix C Rule 8)
        _channel.ConfirmSelect();

        _logger.LogInformation("RabbitMQ publisher initialized on exchange '{Exchange}' with Publisher Confirms enabled", ExchangeName);
    }


    /// <summary>
    /// Publishes an event to the "uav.events" topic exchange with the specified routing key.
    /// Uses Publisher Confirms to guarantee broker acknowledgment before returning.
    /// </summary>
    /// <typeparam name="T">Event record type (e.g., DroneDetectedEvent, DeviceStatusChangedEvent)</typeparam>
    /// <param name="routingKey">Dot-separated routing key (e.g., "device.1002.detection.critical")</param>
    /// <param name="event">The event payload to serialize and publish</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation</param>
    protected Task PublishAsync<T>(string routingKey, T @event, CancellationToken cancellationToken = default)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();

        var body = JsonSerializer.SerializeToUtf8Bytes(@event, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var properties = _channel.CreateBasicProperties();
        properties.ContentType = "application/json";
        properties.DeliveryMode = 2; // Persistent
        properties.MessageId = Guid.NewGuid().ToString();
        properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        properties.Type = typeof(T).Name;

        _channel.BasicPublish(
            exchange: ExchangeName,
            routingKey: routingKey,
            mandatory: true,
            basicProperties: properties,
            body: body);

        // Block until broker confirms receipt — guarantees at-least-once delivery
        _channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));

        _logger.LogDebug("Published {EventType} to '{Exchange}' with key '{RoutingKey}'",
            typeof(T).Name, ExchangeName, routingKey);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _channel?.Close();
            _channel?.Dispose();
            _connection?.Close();
            _connection?.Dispose();
        }

        _disposed = true;
    }
}
