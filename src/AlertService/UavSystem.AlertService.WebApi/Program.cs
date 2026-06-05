using Microsoft.AspNetCore.Server.Kestrel.Core;
using RabbitMQ.Client;
using Scalar.AspNetCore;
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

// ── CORS — required for SignalR negotiate (browser cross-origin) ──────────────
// SignalR requires AllowCredentials() + explicit origins (wildcard not allowed).
// The browser connects from http://localhost:3000 → Kong on :80 → AlertService.
var allowedOrigins = (Environment.GetEnvironmentVariable("CORS_ORIGINS")
    ?? "http://localhost:3000,http://localhost,http://localhost:80")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(opts => opts.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// ── Redis (reverse-lookup: device:meta:{id} → monitor_id) ────────────────────
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var connStr = builder.Configuration.GetConnectionString("RedisConnection") ?? 
                  $"{Environment.GetEnvironmentVariable("REDIS_HOST") ?? "localhost"}:{Environment.GetEnvironmentVariable("REDIS_PORT") ?? "6379"},password={Environment.GetEnvironmentVariable("REDIS_PASSWORD") ?? ""},abortConnect=false";
    return ConnectionMultiplexer.Connect(connStr);
});

// ── RabbitMQ (raw client — NO MassTransit) ───────────────────────────────────
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

// ── Background Event Consumers ───────────────────────────────────────────────
builder.Services.AddHostedService<DroneAlertConsumer>();
builder.Services.AddHostedService<StatusChangeConsumer>();

// ── REST API (health check / Swagger) ────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("UAV Detection System — Alert Service")
               .WithTheme(ScalarTheme.DeepSpace)
               .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

// ── Map SignalR Hub (matches Kong path: /ws/alerts) ──────────────────────────
// UseCors() must come before MapHub() — SignalR negotiate is an HTTP POST.
app.UseCors();
app.MapHub<AlertHub>("/ws/alerts");
app.MapControllers();

app.Run();
