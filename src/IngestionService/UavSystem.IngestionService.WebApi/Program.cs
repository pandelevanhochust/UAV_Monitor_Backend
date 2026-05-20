using System.Threading.Channels;
using ClickHouse.Client.ADO;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using RabbitMQ.Client;
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

// ── Channel<LogPacket> Pipeline (bounded, backpressure-aware) ────────────────
var channel = Channel.CreateBounded<LogPacket>(new BoundedChannelOptions(10_000)
{
    FullMode = BoundedChannelFullMode.DropWrite, // Backpressure: TryWrite returns false
    SingleReader = true,  // Single IngestionWorker consumer
    SingleWriter = false  // Multiple concurrent HTTP requests
});

builder.Services.AddSingleton(channel.Writer);
builder.Services.AddSingleton(channel.Reader);

// ── Redis (device validation + heartbeat — NEVER PostgreSQL) ─────────────────
var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "localhost";
var redisPort = Environment.GetEnvironmentVariable("REDIS_PORT") ?? "6379";
var redisPassword = Environment.GetEnvironmentVariable("REDIS_PASSWORD") ?? "";

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect($"{redisHost}:{redisPort},password={redisPassword},abortConnect=false"));

// ── ClickHouse (log writes — NEVER PostgreSQL) ──────────────────────────────
var clickHouseHost = Environment.GetEnvironmentVariable("CLICKHOUSE_HOST") ?? "localhost";
var clickHousePort = Environment.GetEnvironmentVariable("CLICKHOUSE_PORT") ?? "8123";

builder.Services.AddSingleton(_ =>
    new ClickHouseConnection($"Host={clickHouseHost};Port={clickHousePort};Database=uav_logs"));

// ── gRPC Client → DeviceService (state transitions) ─────────────────────────
var deviceServiceUrl = Environment.GetEnvironmentVariable("DEVICE_SERVICE_GRPC_URL") ?? "http://deviceservice:9081";

builder.Services.AddSingleton(_ =>
{
    var grpcChannel = GrpcChannel.ForAddress(deviceServiceUrl);
    return new InternalDeviceService.InternalDeviceServiceClient(grpcChannel);
});

// ── RabbitMQ (alert publishing — raw client, NO MassTransit) ─────────────────
builder.Services.AddSingleton<IConnectionFactory>(_ =>
    new ConnectionFactory
    {
        HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost",
        Port = int.Parse(Environment.GetEnvironmentVariable("RABBITMQ_PORT") ?? "5672"),
        UserName = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "guest",
        Password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "guest",
        VirtualHost = Environment.GetEnvironmentVariable("RABBITMQ_VHOST") ?? "/"
    });

// ── Background Worker ────────────────────────────────────────────────────────
builder.Services.AddHostedService<IngestionWorker>();

// ── REST API ─────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.Run();
