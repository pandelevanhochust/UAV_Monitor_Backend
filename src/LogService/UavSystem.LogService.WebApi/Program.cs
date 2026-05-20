using ClickHouse.Client.ADO;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Scalar.AspNetCore;
using Serilog;
using UavSystem.LogService.Application.Interfaces;
using UavSystem.LogService.Infrastructure.ClickHouse;
using UavSystem.Shared.Contracts.Grpc;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ──────────────────────────────────────────────────────────────────
builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration)
          .WriteTo.Console()
          .Enrich.WithProperty("Service", "LogService"));

// ── Kestrel: Single-port HTTP (read-only query service) ──────────────────────
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8083, o => o.Protocols = HttpProtocols.Http1AndHttp2);
});

// ── ClickHouse (log reads — NEVER PostgreSQL, Zero Cross-Over) ──────────────
var clickHouseHost = Environment.GetEnvironmentVariable("CLICKHOUSE_HOST") ?? "localhost";
var clickHousePort = Environment.GetEnvironmentVariable("CLICKHOUSE_PORT") ?? "8123";

builder.Services.AddSingleton(_ =>
    new ClickHouseConnection($"Host={clickHouseHost};Port={clickHousePort};Database=uav_logs"));

builder.Services.AddSingleton<ILogRepository, ClickHouseLogRepository>();

// ── gRPC Client → UserService (monitor device scoping) ──────────────────────
var userServiceUrl = Environment.GetEnvironmentVariable("USER_SERVICE_GRPC_URL") ?? "http://userservice:9080";

builder.Services.AddSingleton(_ =>
{
    var grpcChannel = GrpcChannel.ForAddress(userServiceUrl);
    return new UserService.UserServiceClient(grpcChannel);
});

// ── MediatR ──────────────────────────────────────────────────────────────────
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssemblyContaining<UavSystem.LogService.Application.Features.Logs.Queries.GetPaginatedLogsQuery>());

// ── REST API ─────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("UAV Detection System — Log Service")
               .WithTheme(ScalarTheme.DeepSpace)
               .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.MapControllers();
app.Run();
