using System.Threading.Channels;
using ClickHouse.Client.ADO;
using Confluent.Kafka;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using RabbitMQ.Client;
using Scalar.AspNetCore;
using Serilog;
using StackExchange.Redis;
using UavSystem.IngestionService.WebApi.Pipeline;
using UavSystem.IngestionService.WebApi.Pipeline.Models;
using UavSystem.Shared.Contracts.Grpc;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ──────────────────────────────────────────────────────────────────
builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration)
          .WriteTo.Console()
          .Enrich.WithProperty("Service", "IngestionService"));

// ── Kestrel: Single-port HTTP (telemetry ingestion only, no gRPC server) ─────
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8082, o => o.Protocols = HttpProtocols.Http1AndHttp2);
});

// 💡 Sửa thành LogPacket để đồng bộ với cấu trúc Pipeline của bạn
builder.Services.AddSingleton(Channel.CreateUnbounded<LogPacket>(new UnboundedChannelOptions
{
    SingleReader = true, // Chỉ cần 1 bản thể Worker đọc
    SingleWriter = false // Nhiều luồng Controller ghi vào cùng lúc
}));

// ── Kafka Configuration ──────────────────────────────────────────────────────
var telemetryQueueCapacityValue =
    Environment.GetEnvironmentVariable("TELEMETRY_QUEUE_CAPACITY") ??
    builder.Configuration["Ingestion:TelemetryQueueCapacity"];
var telemetryQueueCapacity = int.TryParse(telemetryQueueCapacityValue, out var parsedTelemetryQueueCapacity)
    ? parsedTelemetryQueueCapacity
    : 250000;

builder.Services.AddSingleton(new TelemetryIngestionQueue(telemetryQueueCapacity));
var kafkaBootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS") ?? "localhost:9092";

// 1. Register Kafka Producer  - 1 message =  700bytes
builder.Services.AddSingleton<IProducer<string, string>>(_ =>
{
    var producerConfig = new ProducerConfig
    {
        BootstrapServers = kafkaBootstrapServers, // Sử dụng biến môi trường linh hoạt
        QueueBufferingMaxMessages = 2000000,      // 2tr messages trên localbuffer == 
        LingerMs = 100,                            // Chờ 100ms
        BatchNumMessages = 10000,                 // Gom 10k/ batch 2.5MB
        CompressionType = CompressionType.Lz4     // Nén dữ liệu LZ4 giảm tải băng thông
    };
    
    return new ProducerBuilder<string, string>(producerConfig).Build();
});

// 2. Register Kafka Consumer Configuration (used by IngestionWorker)
builder.Services.AddSingleton(new ConsumerConfig
{
    BootstrapServers = kafkaBootstrapServers,
    GroupId = "ingestion-worker-group",
    AutoOffsetReset = AutoOffsetReset.Earliest,
    EnableAutoCommit = false // Manual commit after DB insert to ensure data safety
});

// ── Redis (device validation + heartbeat) ─────────────────────────────────────
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST");
    var connStr = !string.IsNullOrEmpty(redisHost)
        ? $"{redisHost}:{Environment.GetEnvironmentVariable("REDIS_PORT") ?? "6379"},password={Environment.GetEnvironmentVariable("REDIS_PASSWORD") ?? ""},abortConnect=false"
        : builder.Configuration.GetConnectionString("RedisConnection");
    return ConnectionMultiplexer.Connect(connStr!);
});

// ── ClickHouse (log writes) ──────────────────────────────────────────────────
builder.Services.AddSingleton(_ =>
{
    var chHost = Environment.GetEnvironmentVariable("CLICKHOUSE_HOST");
    var connStr = !string.IsNullOrEmpty(chHost)
        ? $"Host={chHost};Port={Environment.GetEnvironmentVariable("CLICKHOUSE_PORT") ?? "8123"};Username={Environment.GetEnvironmentVariable("CLICKHOUSE_USER") ?? "default"};Password={Environment.GetEnvironmentVariable("CLICKHOUSE_PASSWORD") ?? ""};Database={Environment.GetEnvironmentVariable("CLICKHOUSE_DB") ?? "uav_logs"};"
        : builder.Configuration.GetConnectionString("ClickHouseConnection");
    return new ClickHouseConnection(connStr);
});

// ── gRPC Client → DeviceService (state transitions) ─────────────────────────
var deviceServiceUrl = Environment.GetEnvironmentVariable("DEVICE_SERVICE_GRPC_URL") ?? 
                       builder.Configuration.GetSection("GrpcSettings")["DeviceServiceUrl"] ?? 
                       "http://deviceservice:9081";

builder.Services.AddSingleton(_ =>
{
    var grpcChannel = GrpcChannel.ForAddress(deviceServiceUrl);
    return new InternalDeviceService.InternalDeviceServiceClient(grpcChannel);
});

// ── RabbitMQ (alert publishing — raw client, NO MassTransit) ─────────────────
builder.Services.AddSingleton<IConnectionFactory>(_ =>
{
    var rabbitHost = Environment.GetEnvironmentVariable("RABBITMQ_HOST");
    if (!string.IsNullOrEmpty(rabbitHost))
    {
        return new ConnectionFactory
        {
            HostName = rabbitHost,
            Port = int.Parse(Environment.GetEnvironmentVariable("RABBITMQ_PORT") ?? "5672"),
            UserName = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "guest",
            Password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "guest",
            VirtualHost = Environment.GetEnvironmentVariable("RABBITMQ_VHOST") ?? "/"
        };
    }
    
    var connStr = builder.Configuration.GetConnectionString("RabbitMqConnection");
    return new ConnectionFactory { Uri = new Uri(connStr!) };
});

// ── Background Worker ────────────────────────────────────────────────────────
builder.Services.AddHostedService<Producer>();
builder.Services.AddHostedService<IngestionWorker>();

// ── REST API & Cấu hình khác ──────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("UAV Detection System — Ingestion Service")
               .WithTheme(ScalarTheme.DeepSpace)
               .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.MapControllers();
app.Run();