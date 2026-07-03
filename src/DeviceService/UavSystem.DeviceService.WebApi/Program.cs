using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using Scalar.AspNetCore;
using Serilog;
using StackExchange.Redis;
using UavSystem.DeviceService.Application.Interfaces;
using UavSystem.DeviceService.Domain.Interfaces;
using UavSystem.DeviceService.Infrastructure.BackgroundServices;
using UavSystem.DeviceService.Infrastructure.Caching;
using UavSystem.DeviceService.Infrastructure.Messaging;
using UavSystem.DeviceService.Infrastructure.Persistence;
using UavSystem.DeviceService.Infrastructure.Persistence.Repositories;
using UavSystem.DeviceService.WebApi.GrpcServices;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ──────────────────────────────────────────────────────────────────
builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration)
          .WriteTo.Console()
          .Enrich.WithProperty("Service", "DeviceService"));

// ── Kestrel: Dual-port (HTTP REST + gRPC) ────────────────────────────────────
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8081, o => o.Protocols = HttpProtocols.Http1AndHttp2);
    options.ListenAnyIP(9081, o => o.Protocols = HttpProtocols.Http2);
});

// ── Database (PostgreSQL via EF Core) ────────────────────────────────────────
var postgresHost = Environment.GetEnvironmentVariable("POSTGRES_HOST");
var connectionString = !string.IsNullOrEmpty(postgresHost)
    ? $"Host={postgresHost};" +
      $"Port={Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432"};" +
      $"Database={Environment.GetEnvironmentVariable("POSTGRES_DB_DEVICE") ?? "uav_device_db"};" +
      $"Username={Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "uav_admin"};" +
      $"Password={Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? ""};" +
      "Include Error Detail=true;"
    : builder.Configuration.GetConnectionString("PostgresConnection");

builder.Services.AddDbContext<DeviceDbContext>(options =>
    options.UseNpgsql(connectionString));

// ── Redis (StackExchange.Redis — singleton IConnectionMultiplexer) ────────────
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST");
    var connStr = !string.IsNullOrEmpty(redisHost)
        ? $"{redisHost}:{Environment.GetEnvironmentVariable("REDIS_PORT") ?? "6379"},password={Environment.GetEnvironmentVariable("REDIS_PASSWORD") ?? ""},abortConnect=false"
        : builder.Configuration.GetConnectionString("RedisConnection");
    return ConnectionMultiplexer.Connect(connStr);
});

// ── RabbitMQ (raw RabbitMQ.Client — NO MassTransit) ──────────────────────────
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

// ── Dependency Injection ─────────────────────────────────────────────────────
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
builder.Services.AddSingleton<IRedisDeviceSyncService, RedisDeviceSyncService>();
builder.Services.AddSingleton<DeviceStatusPublisher>();

// ── MediatR + FluentValidation ───────────────────────────────────────────────
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssemblyContaining<UavSystem.DeviceService.Application.Features.Devices.Commands.RegisterDeviceCommand>());
builder.Services.AddValidatorsFromAssemblyContaining<UavSystem.DeviceService.Application.Features.Devices.Commands.RegisterDeviceCommand>();

// ── gRPC Server ──────────────────────────────────────────────────────────────
builder.Services.AddGrpc();

// ── Background Services ──────────────────────────────────────────────────────
builder.Services.AddHostedService<RedisHeartbeatWatcherService>();
builder.Services.AddHostedService<RedisStartupSyncWorker>();

// ── REST API ─────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

// ── Middleware Pipeline ──────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("UAV Detection System — Device Service")
               .WithTheme(ScalarTheme.DeepSpace)
               .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.MapControllers();
app.MapGrpcService<DeviceGrpcService>();

// ── Auto-migrate on startup (dev only) ───────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DeviceDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
