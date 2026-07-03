public class AlertRabbitMQPublisherWorker : BackgroundService
{
    private readonly Channel<TelemetryPayload> _alertChannel;
    private readonly IModel _rabbitChannel; // Cấu hình RabbitMQ Channel của bạn

    public AlertRabbitMQPublisherWorker(Channel<TelemetryPayload> alertChannel, IModel rabbitChannel)
    {
        _alertChannel = alertChannel;
        _rabbitChannel = rabbitChannel;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Luồng ngầm này liên tục đọc từ RAM Channel cho đến khi app tắt
        await foreach (var payload in _alertChannel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
                
                // Đẩy lập tức từng bản tin Alert sang RabbitMQ để Dashboard nhận Real-time dưới 10ms
                _rabbitChannel.BasicPublish(
                    exchange: "uav.alerts",
                    routingKey: "drone.detected",
                    basicProperties: null,
                    body: body);
            }
            catch (Exception ex)
            {
                // Log lỗi kết nối RabbitMQ nếu có nhưng KHÔNG làm sập tầng nhận HTTP
            }
        }
    }
}