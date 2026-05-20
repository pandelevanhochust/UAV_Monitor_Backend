using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace UavSystem.Shared.Infrastructure.Messaging;

/// <summary>
/// Abstract BackgroundService-derived base class for RabbitMQ consumers using raw RabbitMQ.Client.
/// Enforces:
///   - Queue declaration + binding to exchange "uav.events" with configurable routing key
///   - Manual ACK only (autoAck: false) per Appendix C Rule 9
///   - BasicAck on successful processing, BasicNack(requeue: true) on failure
///   - Dead Letter Exchange routing to "q.malformed.dlq" for poison messages
///
/// MassTransit is STRICTLY FORBIDDEN — all messaging uses raw RabbitMQ.Client.
/// </summary>
/// <typeparam name="T">The event record type to deserialize from the message body</typeparam>
public abstract class RabbitMqConsumerBase<T> : BackgroundService where T : class
{
    private const string ExchangeName = "uav.events";
    private const string DeadLetterExchange = "uav.events.dlx";
    private const string DeadLetterQueue = "q.malformed.dlq";

    private readonly IConnectionFactory _connectionFactory;
    private readonly ILogger _logger;
    private IConnection? _connection;
    private IModel? _channel;

    /// <summary>The queue name this consumer binds to (e.g., "q.alert.realtime").</summary>
    protected abstract string QueueName { get; }

    /// <summary>The routing key pattern for binding (e.g., "device.*.detection.critical").</summary>
    protected abstract string RoutingKey { get; }

    protected RabbitMqConsumerBase(IConnectionFactory connectionFactory, ILogger logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _connection = _connectionFactory.CreateConnection();
        _channel = _connection.CreateModel();

        // Declare Dead Letter Exchange + Queue for poison messages
        _channel.ExchangeDeclare(
            exchange: DeadLetterExchange,
            type: "fanout",
            durable: true,
            autoDelete: false);

        _channel.QueueDeclare(
            queue: DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false);

        _channel.QueueBind(
            queue: DeadLetterQueue,
            exchange: DeadLetterExchange,
            routingKey: string.Empty);

        // Declare primary exchange
        _channel.ExchangeDeclare(
            exchange: ExchangeName,
            type: "topic",
            durable: true,
            autoDelete: false);

        // Declare consumer queue with DLX routing
        var queueArgs = new Dictionary<string, object>
        {
            { "x-dead-letter-exchange", DeadLetterExchange }
        };

        _channel.QueueDeclare(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArgs);

        _channel.QueueBind(
            queue: QueueName,
            exchange: ExchangeName,
            routingKey: RoutingKey);

        // Prefetch: process one message at a time for ordered, reliable processing
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += async (_, ea) =>
        {
            try
            {
                var body = Encoding.UTF8.GetString(ea.Body.Span);
                var message = JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (message is null)
                {
                    _logger.LogWarning("Failed to deserialize message from queue '{Queue}', sending to DLQ", QueueName);
                    _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
                    return;
                }

                await HandleMessageAsync(message, stoppingToken);

                // Manual ACK only after successful processing (Appendix C Rule 9)
                _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);

                _logger.LogDebug("Successfully processed {EventType} from queue '{Queue}'",
                    typeof(T).Name, QueueName);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown — requeue the message for another consumer
                _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message from queue '{Queue}', requeuing", QueueName);
                // BasicNack with requeue: true on failure (Appendix C Rule 9)
                _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        // autoAck: false — Manual ACK is non-negotiable
        _channel.BasicConsume(
            queue: QueueName,
            autoAck: false,
            consumer: consumer);

        _logger.LogInformation("RabbitMQ consumer started on queue '{Queue}' with routing key '{RoutingKey}'",
            QueueName, RoutingKey);

        // Keep the BackgroundService alive until cancellation
        return Task.Delay(Timeout.Infinite, stoppingToken)
            .ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnCanceled);
    }

    /// <summary>
    /// Process a deserialized message. Implementations must not catch exceptions
    /// they cannot handle — let them propagate so the base class can BasicNack.
    /// </summary>
    /// <param name="message">The deserialized event record</param>
    /// <param name="cancellationToken">Cancellation token for cooperative shutdown</param>
    protected abstract Task HandleMessageAsync(T message, CancellationToken cancellationToken);

    public override void Dispose()
    {
        _channel?.Close();
        _channel?.Dispose();
        _connection?.Close();
        _connection?.Dispose();
        base.Dispose();
    }
}
