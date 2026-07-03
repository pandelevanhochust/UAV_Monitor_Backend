using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels; 
using Microsoft.Extensions.Hosting;
using System.Text.Json; 
using RabbitMQ.Client; 

// 💡 Đổi dòng using trỏ đúng vào thư mục Models nội bộ hiển thị trong ảnh image_c1c521.png
using UavSystem.IngestionService.WebApi.Pipeline.Models; 

namespace UavSystem.IngestionService.WebApi.Pipeline
{
    public class AlertRabbitMQPublisherWorker : BackgroundService
    {
        // 💡 Đổi sang LogPacket theo đúng cấu trúc của bạn
        private readonly Channel<LogPacket> _alertChannel; 
        private readonly IModel _rabbitChannel; 

        public AlertRabbitMQPublisherWorker(Channel<LogPacket> alertChannel, IModel rabbitChannel)
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
                    
                    // Đẩy lập tức từng bản tin Alert sang RabbitMQ
                    _rabbitChannel.BasicPublish(
                        exchange: "uav.alerts",
                        routingKey: "drone.detected",
                        basicProperties: null,
                        body: body);
                }
                catch (Exception ex)
                {
                    // Log lỗi nếu kết nối RabbitMQ gặp sự cố
                }
            }
        }
    }
}