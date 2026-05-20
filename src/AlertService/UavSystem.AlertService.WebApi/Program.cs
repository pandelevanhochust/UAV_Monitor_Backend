using Microsoft.AspNetCore.Server.Kestrel.Core;
using RabbitMQ.Client;
using Serilog;
using StackExchange.Redis;
using UavSystem.AlertService.WebApi.Consumers;
using UavSystem.AlertService.WebApi.Hubs;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ──────────────────────────────────────────────────────────────────
builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration)
          .WriteTo.Console()
          .Enrich.WithProperty("Service", "AlertService"));

// ── Kestrel: Single-port HTTP for REST + WebSocket ───────────────────────────
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8084, o => o.Protocols = HttpProtocols.Http1AndHttp2);
});

// ── SignalR ──────────────────────────────────────────────────────────────────
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
});

// ── Redis (reverse-lookup: device:meta:{id} → monitor_id) ────────────────────
var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "localhost";
var redisPort = Environment.GetEnvironmentVariable("REDIS_PORT") ?? "6379";
var redisPassword = Environment.GetEnvironmentVariable("REDIS_PASSWORD") ?? "";

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect($"{redisHost}:{redisPort},password={redisPassword},abortConnect=false"));

// ── RabbitMQ (raw client — NO MassTransit) ───────────────────────────────────
builder.Services.AddSingleton<IConnectionFactory>(_ =>
    new ConnectionFactory
    {
        HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost",
        Port = int.Parse(Environment.GetEnvironmentVariable("RABBITMQ_PORT") ?? "5672"),
        UserName = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "guest",
        Password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "guest",
        VirtualHost = Environment.GetEnvironmentVariable("RABBITMQ_VHOST") ?? "/"
    });

// ── Background Event Consumers ───────────────────────────────────────────────
builder.Services.AddHostedService<DroneAlertConsumer>();
builder.Services.AddHostedService<StatusChangeConsumer>();

// ── REST API (health check / Swagger) ────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ── Map SignalR Hub (matches Kong path: /ws/alerts) ──────────────────────────
app.MapHub<AlertHub>("/ws/alerts");
app.MapControllers();

app.Run();
