using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Serilog;
using UavSystem.UserService.Application.Interfaces;
using UavSystem.UserService.Domain.Interfaces;
using UavSystem.UserService.Infrastructure.Auth;
using UavSystem.UserService.Infrastructure.Persistence;
using UavSystem.UserService.Infrastructure.Persistence.Repositories;
using UavSystem.UserService.WebApi.GrpcServices;
using UavSystem.UserService.WebApi.Middleware;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ──────────────────────────────────────────────────────────────────
builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration)
          .WriteTo.Console()
          .Enrich.WithProperty("Service", "UserService"));

// ── Kestrel: Dual-port (HTTP REST + gRPC) ────────────────────────────────────
builder.WebHost.ConfigureKestrel(options =>
{
    // HTTP/1.1 + HTTP/2 for REST endpoints
    options.ListenAnyIP(8080, o => o.Protocols = HttpProtocols.Http1AndHttp2);
    // HTTP/2 only for gRPC (Kong ForwardAuth target)
    options.ListenAnyIP(9080, o => o.Protocols = HttpProtocols.Http2);
});

// ── Database (PostgreSQL via EF Core) ────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("PostgresConnection") ?? 
                       $"Host={Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost"};" +
                       $"Port={Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432"};" +
                       $"Database={Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "uav_system"};" +
                       $"Username={Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "uav_admin"};" +
                       $"Password={Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? ""}";

builder.Services.AddDbContext<UserDbContext>(options =>
    options.UseNpgsql(connectionString));

// ── JWT Configuration (IOptions<T> pattern — never hardcode secrets) ─────────
builder.Services.Configure<JwtSettings>(settings =>
{
    settings.Secret = Environment.GetEnvironmentVariable("JWT_SECRET") ?? "super_secret_32_character_signing_key_boundary_123!";
    settings.Issuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "uav-detection-system";
    settings.Audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "uav-supervisors";
    settings.ExpiresSeconds = int.Parse(Environment.GetEnvironmentVariable("JWT_EXPIRES_SECONDS") ?? "86400");
});

// ── Dependency Injection ─────────────────────────────────────────────────────
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddSingleton<IJwtService, JwtService>();

// ── MediatR + FluentValidation ───────────────────────────────────────────────
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssemblyContaining<UavSystem.UserService.Application.Features.Auth.Commands.LoginCommand>());
builder.Services.AddValidatorsFromAssemblyContaining<UavSystem.UserService.Application.Features.Auth.Commands.LoginCommandValidator>();

// ── gRPC Server ──────────────────────────────────────────────────────────────
builder.Services.AddGrpc();

// ── REST API ─────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

// ── Middleware Pipeline ──────────────────────────────────────────────────────
app.UseMiddleware<ExceptionHandlerMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("UAV Detection System API Control Plane")
               .WithTheme(ScalarTheme.DeepSpace) // Giao diện tối hiện đại, rất hợp với đồ án quân sự/UAV
               .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient); // Tự sinh code ví dụ bằng C#
    });
}

app.MapControllers();
app.MapGrpcService<UserGrpcService>();

// ── Auto-migrate on startup (dev only) ───────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<UserDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
