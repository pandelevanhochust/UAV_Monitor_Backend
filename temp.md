# claude.md — UAV Drone Detection System: AI Coding Assistant Source of Truth

> **STRICT INSTRUCTION FOR AI ASSISTANT**: This document is the **absolute source of truth**. You must never deviate from the architectural decisions, naming conventions, technology choices, or structural patterns defined here. Do not hallucinate libraries, shift paradigms, invent abstractions, or add patterns not explicitly described. Every file, class, interface, and configuration you generate must trace back to a decision in this document. When in doubt, **do less and ask**, rather than guess.

---

## 1. PROJECT OVERVIEW

### Purpose

This backend powers a real-time AIoT (AI + IoT) system that detects unauthorized UAV/Drone activity by processing RF (Radio Frequency) signals from distributed radar hardware nodes deployed at perimeter locations. The system ingests high-frequency telemetry from physical radar devices, correlates signal patterns to identify DJI and generic flycam signatures, and delivers sub-second alerts to authorized monitoring personnel via WebSocket push.

### Core Business Logic Goals

1. **High-Throughput RF Telemetry Ingestion**: A dedicated Ingestion pipeline receives radar log packets at high frequency from edge hardware over HTTPS. Each packet is authenticated via a device-scoped API Key (`X-Device-API-Key` header), validated in sub-millisecond time using Redis cache, and written to ClickHouse for time-series storage. This is the system's **Hot Path** and must never block on synchronous database calls.

2. **Real-Time Drone Detection Alerts**: When a telemetry packet carries `detected = true`, the system immediately publishes a structured event to a RabbitMQ Topic Exchange. A dedicated consumer service picks this up and pushes a WebSocket notification to all authorized monitoring browser clients using SignalR within milliseconds.

3. **Device Lifecycle & Health Tracking**: All radar hardware devices are registered and assigned to monitor users by an Admin. Device health (ONLINE / OFFLINE / ERROR) is tracked via a Redis heartbeat TTL mechanism. If a device stops sending data for 10 minutes, the system automatically marks it OFFLINE without requiring the device to send a shutdown signal — a critical requirement for handling physical hardware failure.

4. **Strict Role-Based Access Control (RBAC)**: Two roles exist — `ADMIN` (full system control: user management, device registration, assignment) and `MONITOR` (read-only: views logs, receives alerts, monitors assigned devices only). All API endpoints enforce this boundary via JWT claims.

5. **Analytics & Historical Reporting**: Monitors can query historical detection logs, frequency charts, and drone-type breakdowns for their assigned devices. All analytical reads are served from ClickHouse directly, bypassing PostgreSQL entirely.

---

## 2. SYSTEM ARCHITECTURE & TRAEFIK API GATEWAY BOUNDARIES

### 2.1 Microservice Inventory

| Service Name       | Project Type                   | Responsibility                                                                                                |
| ------------------ | ------------------------------ | ------------------------------------------------------------------------------------------------------------- |
| `UserService`      | ASP.NET Core Web API           | Authentication, JWT issuance, RBAC enforcement, user CRUD, device-to-monitor assignment queries               |
| `DeviceService`    | ASP.NET Core Web API           | Device registration, status management, Admin-driven assignment, Write-Through cache sync to Redis            |
| `IngestionService` | ASP.NET Core Worker (Pipeline) | Hot-path telemetry receiver, API key auth via Redis, ClickHouse write, RabbitMQ publish, heartbeat management |
| `LogService`       | ASP.NET Core Web API           | CQRS Read — serves historical log queries and analytics from ClickHouse to authorized monitors                |
| `AlertService`     | ASP.NET Core Worker            | RabbitMQ consumer (`q.alert.realtime`), SignalR hub host, WebSocket push to browser clients                   |

### 2.2 Traefik API Gateway (Docker Provider)

**Traefik Version**: v3.x  
**Mode**: Docker provider with dynamic label-based routing  
**Entrypoints**:

- `web` → `:80` (redirect to HTTPS in production; used directly in local dev)
- `websecure` → `:443` (TLS termination in production)

Traefik is the **sole public-facing ingress**. All microservices are on an internal Docker bridge network (`uav_network`) and are **NOT** exposed on host ports. Only Traefik ports 80/443 are published.

#### Traefik Routing Matrix

| Path Prefix       | Target Service           | Strip Prefix         | Middlewares                | Notes                                                             |
| ----------------- | ------------------------ | -------------------- | -------------------------- | ----------------------------------------------------------------- |
| `/api/v1/auth`    | `user-service:8080`      | Yes (`/api/v1/auth`) | `ratelimit`, `cors`        | Login, register, token refresh                                    |
| `/api/v1/users`   | `user-service:8080`      | Yes                  | `jwt-auth`, `cors`         | Admin-only user management                                        |
| `/api/v1/devices` | `device-service:8080`    | Yes                  | `jwt-auth`, `cors`         | Admin device CRUD and assignment                                  |
| `/api/v1/ingest`  | `ingestion-service:8080` | Yes                  | `device-key-check`, `cors` | Device-facing, no JWT; uses X-Device-API-Key header               |
| `/api/v1/logs`    | `log-service:8080`       | Yes                  | `jwt-auth`, `cors`         | Monitor/Admin historical log queries                              |
| `/hubs/alerts`    | `alert-service:8080`     | No                   | `cors`, `ws-upgrade`       | WebSocket endpoint — do NOT strip prefix; SignalR needs full path |

#### Traefik Docker Labels Template (per service in `docker-compose.yml`)

```yaml
# Example: UserService
labels:
  - "traefik.enable=true"
  - "traefik.http.routers.user-service.rule=PathPrefix(`/api/v1/auth`) || PathPrefix(`/api/v1/users`)"
  - "traefik.http.routers.user-service.entrypoints=web"
  - "traefik.http.services.user-service.loadbalancer.server.port=8080"
  - "traefik.http.middlewares.strip-user.stripprefix.prefixes=/api/v1/auth,/api/v1/users"
  - "traefik.http.routers.user-service.middlewares=strip-user,cors-mw"
```

#### Defined Traefik Middlewares (in `traefik/dynamic/middlewares.yml`)

```yaml
http:
  middlewares:
    cors-mw:
      headers:
        accessControlAllowMethods: ["GET", "POST", "PUT", "DELETE", "OPTIONS"]
        accessControlAllowOriginList: ["*"]
        accessControlAllowHeaders:
          ["Authorization", "Content-Type", "X-Device-API-Key"]
    ratelimit:
      rateLimit:
        average: 100
        burst: 50
    jwt-auth:
      # Traefik ForwardAuth to UserService /internal/validate
      forwardAuth:
        address: "http://user-service:8080/internal/validate"
        authResponseHeaders: ["X-User-Id", "X-User-Role"]
    ws-upgrade:
      headers:
        customRequestHeaders:
          Connection: "Upgrade"
          Upgrade: "websocket"
```

### 2.3 Internal Service Communication

| From               | To              | Protocol                 | Purpose                                                                                       |
| ------------------ | --------------- | ------------------------ | --------------------------------------------------------------------------------------------- |
| `IngestionService` | `DeviceService` | gRPC (HTTP/2)            | Report device status transitions (OFFLINE → ONLINE, ERROR)                                    |
| `AlertService`     | `UserService`   | gRPC (HTTP/2)            | Resolve which SignalR connection groups belong to a monitor user                              |
| `DeviceService`    | Redis           | StackExchange.Redis      | Write-Through: update `device:meta` and `monitor:devices` on assignment change                |
| `IngestionService` | Redis           | StackExchange.Redis      | API key lookup, heartbeat refresh, latest log state update                                    |
| `IngestionService` | ClickHouse      | ClickHouse.Client (HTTP) | Bulk-insert telemetry logs                                                                    |
| `IngestionService` | RabbitMQ        | RabbitMQ.Client          | Publish `device.{id}.detection.critical` events                                               |
| `AlertService`     | RabbitMQ        | RabbitMQ.Client          | Consume `q.alert.realtime` queue                                                              |
| `LogService`       | ClickHouse      | ClickHouse.Client (HTTP) | Read-only analytical queries                                                                  |
| Any service        | Any service     | **Never direct HTTP**    | Services never call each other's public REST APIs internally; use gRPC or message broker only |

#### gRPC Proto Contracts (live in `src/Shared/Shared.Contracts/Protos/`)

```protobuf
// device_service.proto
service DeviceRpc {
  rpc UpdateDeviceStatus (UpdateStatusRequest) returns (UpdateStatusReply);
  rpc GetDevicesByMonitor (GetDevicesByMonitorRequest) returns (GetDevicesByMonitorReply);
}

// user_service.proto
service UserRpc {
  rpc ValidateToken (ValidateTokenRequest) returns (ValidateTokenReply);
  rpc GetMonitorConnections (GetMonitorConnectionsRequest) returns (GetMonitorConnectionsReply);
}
```

---

## 3. TECH STACK & .NET ARCHITECTURAL CONVENTIONS

### 3.1 Language & Runtime

- **Language**: C# 12
- **Runtime**: .NET 9 (use `net9.0` as target framework across all projects)
- **Nullable Reference Types**: Enabled globally in all projects (`<Nullable>enable</Nullable>`)
- **Implicit Usings**: Enabled (`<ImplicitUsings>enable</ImplicitUsings>`)
- **Treat Warnings as Errors**: Enabled in CI (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`)

### 3.2 Architectural Pattern: Clean Architecture (per microservice)

Each microservice that is a Web API or Worker is structured as a **4-project Clean Architecture vertical slice**:

```
ServiceName/
├── ServiceName.Domain/          # Entities, Value Objects, Domain Events, Enums, Interfaces (no dependencies)
├── ServiceName.Application/     # Use Cases (Commands/Queries via MediatR), DTOs, Validators (FluentValidation), IRepository interfaces
├── ServiceName.Infrastructure/  # EF Core DbContext, Repository impls, Redis, RabbitMQ, gRPC clients, ClickHouse client
└── ServiceName.WebAPI/          # ASP.NET Core host (or Worker host), Controllers, Minimal API endpoints, SignalR Hubs, DI composition root
```

**Dependency Rule (strictly enforced)**:

- `Domain` → no dependencies
- `Application` → depends on `Domain` only
- `Infrastructure` → depends on `Application` and `Domain`
- `WebAPI` → depends on `Infrastructure` and `Application`
- `Shared.*` projects → depend on nothing within this solution (standalone contracts only)

### 3.3 Key NuGet Packages (Canonical List — use ONLY these, no alternatives)

| Package                                          | Version Constraint           | Used In                                                         |
| ------------------------------------------------ | ---------------------------- | --------------------------------------------------------------- |
| `MediatR`                                        | 12.x                         | Application layer (CQRS handlers)                               |
| `FluentValidation.DependencyInjectionExtensions` | 11.x                         | Application layer (request validation)                          |
| `Microsoft.EntityFrameworkCore`                  | 9.x                          | Infrastructure (UserService, DeviceService)                     |
| `Npgsql.EntityFrameworkCore.PostgreSQL`          | 9.x                          | Infrastructure (PostgreSQL EF provider)                         |
| `StackExchange.Redis`                            | 2.x                          | Infrastructure (all Redis operations)                           |
| `RabbitMQ.Client`                                | 6.x                          | Infrastructure (IngestionService publish, AlertService consume) |
| `ClickHouse.Client`                              | latest stable                | Infrastructure (IngestionService write, LogService read)        |
| `Grpc.AspNetCore`                                | 2.x                          | WebAPI projects that host gRPC services                         |
| `Grpc.Net.Client`                                | 2.x                          | Infrastructure projects that call gRPC                          |
| `Google.Protobuf`                                | 3.x                          | Shared.Contracts                                                |
| `Grpc.Tools`                                     | 2.x                          | Shared.Contracts (proto codegen)                                |
| `Microsoft.AspNetCore.SignalR`                   | (included in ASP.NET Core 9) | AlertService.WebAPI                                             |
| `Serilog.AspNetCore`                             | 8.x                          | All WebAPI/Worker hosts                                         |
| `Serilog.Sinks.Console`                          | 5.x                          | All hosts                                                       |
| `OpenTelemetry.Extensions.Hosting`               | 1.x                          | All hosts (traces + metrics)                                    |
| `Swashbuckle.AspNetCore`                         | 7.x                          | All Web API hosts (dev only)                                    |

### 3.4 Configuration Convention

- All service configuration uses `appsettings.json` + environment variable overrides (Docker Compose `environment:` block).
- **Never** hardcode connection strings. Use `IOptions<T>` pattern with strongly-typed config classes in `Infrastructure`.
- Config class naming: `PostgresOptions`, `RedisOptions`, `RabbitMqOptions`, `ClickHouseOptions`, `JwtOptions`.
- Environment variable override format: `SECTION__KEY` (double underscore for nested keys).

### 3.5 Logging & Observability

- **Structured Logging**: Serilog with Console sink (JSON format in production containers). Output template must include `{Timestamp:o}`, `{Level:u3}`, `{SourceContext}`, `{Message}`, `{Exception}`.
- **Correlation**: Every incoming HTTP request gets a `X-Correlation-Id` header (generated by Traefik middleware if absent). Propagate via `ILogger` scope.
- **Health Checks**: Every service exposes `/health` (liveness) and `/health/ready` (readiness). Register with `AddHealthChecks()`. Traefik does NOT route `/health` — it is for Docker health checks only.
- **Metrics**: OpenTelemetry SDK; export to console in dev. Production target (outside this scope) would be Prometheus.

---

## 4. DATABASE & STORAGE STRATEGY

### 4.1 PostgreSQL (EF Core) — UserService & DeviceService

**Connection**: Npgsql EF Core provider. Connection string from `PostgresOptions.ConnectionString`.

**Migrations**: Code-first. All migrations live in the `Infrastructure` project of each service. Apply at startup via `dbContext.Database.MigrateAsync()` in `Program.cs` for dev; in production, use a dedicated migration job.

**UserService DbContext** (`UserDbContext : DbContext`):

```csharp
// Entity: ApplicationUser
public class ApplicationUser
{
    public Guid Id { get; set; }                    // PK
    public string Name { get; set; } = default!;
    public string Email { get; set; } = default!;   // Unique Index
    public string PasswordHash { get; set; } = default!;
    public UserRole Role { get; set; }              // Enum: MONITOR, ADMIN
}

// Enum (stored as string in DB via .HasConversion<string>())
public enum UserRole { MONITOR, ADMIN }
```

**DeviceService DbContext** (`DeviceDbContext : DbContext`):

```csharp
// Entity: Device
public class Device
{
    public long DeviceId { get; set; }                   // PK
    public string LocationName { get; set; } = default!;
    public DeviceStatus Status { get; set; }             // Enum: ONLINE, OFFLINE, ERROR
    public Guid? AssignedMonitorId { get; set; }         // FK -> ApplicationUser.Id (Nullable, Indexed)
    public DateTimeOffset UpdatedAt { get; set; }
}

// Enum (stored as string)
public enum DeviceStatus { ONLINE, OFFLINE, ERROR }
```

**EF Core Configuration Notes**:

- Use `IEntityTypeConfiguration<T>` in separate files per entity, registered via `modelBuilder.ApplyConfigurationsFromAssembly(...)`.
- `Email` gets `HasIndex(u => u.Email).IsUnique()`.
- `AssignedMonitorId` gets `HasIndex(d => d.AssignedMonitorId)`.
- All enums stored as strings: `.HasConversion<string>()`.
- `UpdatedAt` gets `HasDefaultValueSql("NOW()")`.

### 4.2 ClickHouse — IngestionService (Write) & LogService (Read)

**Client**: `ClickHouse.Client` NuGet package using HTTP transport.  
**Table**: `drone_logs` (created via DDL migration script, not EF Core).

```sql
CREATE TABLE IF NOT EXISTS drone_logs (
    device_id    Int64,
    timeStamp    DateTime64(3, 'UTC'),
    location     String,
    status       LowCardinality(String),
    detected     UInt8,
    drone_type   LowCardinality(String),
    controlState String,
    accuracy     Float32
)
ENGINE = MergeTree()
ORDER BY (device_id, timeStamp)
PARTITION BY toYYYYMM(timeStamp);
```

**DDL Script Location**: `infra/clickhouse/init.sql` — executed on ClickHouse container startup via Docker volume mount.

**Write Pattern (IngestionService)**: Use bulk insert with `ClickHouseConnection.CreateColumnWriter()`. Never insert row-by-row. Batch incoming packets in a `Channel<LogEntry>` and flush every 500ms or 100 records, whichever comes first.

**Read Pattern (LogService)**: Parameterized queries via `ClickHouseCommand`. No ORM. Return raw DTOs. All queries must include `device_id` in the WHERE clause to leverage MergeTree ordering.

### 4.3 Redis — Distributed Cache & Event State

**Client**: `StackExchange.Redis` via `IConnectionMultiplexer` (singleton, registered once in DI).

**Key Schema (strictly follow — no deviations)**:

| Purpose              | Key Pattern                     | Redis Type | Value                                         | TTL              |
| -------------------- | ------------------------------- | ---------- | --------------------------------------------- | ---------------- |
| Device Metadata      | `device:meta:{device_id}`       | Hash       | Fields: `status`, `monitor_id`, `api_key`     | None (permanent) |
| Device Heartbeat     | `device:heartbeat:{device_id}`  | String     | `"active"`                                    | 600s (10 min)    |
| Latest Log State     | `device:latest_log:{device_id}` | Hash       | Fields: `timestamp`, `detected`, `drone_type` | None (permanent) |
| Monitor Device Index | `monitor:devices:{user_id}`     | Set        | Members: `device_id` values (as strings)      | None (permanent) |

**Keyspace Expiry Notifications**: Redis must be configured with `notify-keyspace-events Ex` to emit events when `device:heartbeat` keys expire. A `BackgroundService` in `DeviceService` (or a dedicated `HeartbeatWatcherService`) subscribes to `__keyevent@0__:expired` on the Redis pub/sub channel and triggers the OFFLINE transition flow.

**Write-Through Policy (DeviceService)**: When Admin assigns or updates a device, `DeviceService` writes to Postgres **and** immediately updates Redis (`device:meta`, `monitor:devices`) in the same operation. Redis is never stale for device metadata because the Ingestion hot path depends on it.

### 4.4 RabbitMQ

**Client**: `RabbitMQ.Client` (not MassTransit — use raw client for explicit control).

**Exchange Configuration**:

```
Name: uav.events
Type: topic
Durable: true
AutoDelete: false
```

**Queue Configuration**:

| Queue                   | Binding Key                   | Arguments                              | Consumer                |
| ----------------------- | ----------------------------- | -------------------------------------- | ----------------------- |
| `q.alert.realtime`      | `device.*.detection.critical` | `x-queue-type: classic`, durable: true | AlertService            |
| `q.telemetry.analytics` | `device.#`                    | `x-queue-mode: lazy`, durable: true    | Future analytics worker |

**Message Format**: JSON-serialized `DroneDetectionEvent` from `Shared.Events`:

```csharp
public record DroneDetectionEvent(
    long DeviceId,
    string Location,
    string DroneType,
    string ControlState,
    float Accuracy,
    DateTime DetectedAt
);
```

**ACK Policy**: `q.alert.realtime` uses **manual ACK** (`autoAck: false`). `BasicAck` only after SignalR push confirmed. `BasicNack` with `requeue: true` on failure.

---

## 5. INITIAL REPOSITORY DIRECTORY TREE

```
uav-drone-detection/                          ← Git repo root
│
├── uav-drone-detection.sln                   ← Global solution file (all projects referenced here)
│
├── claude.md                                 ← This file
├── .gitignore                                ← Standard .NET + Docker .gitignore
├── .editorconfig                             ← C# formatting rules (4-space indent, UTF-8, etc.)
│
├── docker-compose.yml                        ← Full local dev stack (all services + infra)
├── docker-compose.override.yml               ← Dev overrides (volume mounts, debug ports)
├── .env                                      ← Environment variable defaults (gitignored)
├── .env.example                              ← Committed example env file
│
├── traefik/
│   ├── traefik.yml                           ← Static Traefik config (entrypoints, providers, dashboard)
│   └── dynamic/
│       └── middlewares.yml                   ← Dynamic middleware definitions (cors, ratelimit, etc.)
│
├── infra/
│   ├── clickhouse/
│   │   └── init.sql                          ← ClickHouse DDL for drone_logs table
│   ├── postgres/
│   │   └── init.sql                          ← Optional: Postgres role/db creation script
│   └── rabbitmq/
│       └── definitions.json                  ← RabbitMQ pre-configured exchange + queue definitions
│
├── src/
│   │
│   ├── Shared/
│   │   ├── Shared.Contracts/                 ← Shared NuGet-style Class Library
│   │   │   ├── Shared.Contracts.csproj
│   │   │   ├── Protos/
│   │   │   │   ├── device_service.proto
│   │   │   │   └── user_service.proto
│   │   │   └── Grpc/                         ← Generated gRPC stubs (auto via Grpc.Tools)
│   │   │
│   │   └── Shared.Events/                    ← Shared event/message record types
│   │       ├── Shared.Events.csproj
│   │       └── DroneDetectionEvent.cs
│   │
│   ├── UserService/
│   │   ├── UserService.Domain/
│   │   │   ├── UserService.Domain.csproj
│   │   │   ├── Entities/
│   │   │   │   └── ApplicationUser.cs
│   │   │   ├── Enums/
│   │   │   │   └── UserRole.cs
│   │   │   └── Interfaces/
│   │   │       └── IUserRepository.cs
│   │   │
│   │   ├── UserService.Application/
│   │   │   ├── UserService.Application.csproj
│   │   │   ├── Commands/
│   │   │   │   ├── RegisterUser/
│   │   │   │   │   ├── RegisterUserCommand.cs
│   │   │   │   │   ├── RegisterUserCommandHandler.cs
│   │   │   │   │   └── RegisterUserCommandValidator.cs
│   │   │   │   └── LoginUser/
│   │   │   │       ├── LoginUserCommand.cs
│   │   │   │       └── LoginUserCommandHandler.cs
│   │   │   ├── Queries/
│   │   │   │   └── GetAllUsers/
│   │   │   │       ├── GetAllUsersQuery.cs
│   │   │   │       └── GetAllUsersQueryHandler.cs
│   │   │   └── DTOs/
│   │   │       ├── UserDto.cs
│   │   │       └── LoginResponseDto.cs
│   │   │
│   │   ├── UserService.Infrastructure/
│   │   │   ├── UserService.Infrastructure.csproj
│   │   │   ├── Persistence/
│   │   │   │   ├── UserDbContext.cs
│   │   │   │   ├── Configurations/
│   │   │   │   │   └── ApplicationUserConfiguration.cs
│   │   │   │   ├── Migrations/                         ← EF Core auto-generated
│   │   │   │   └── Repositories/
│   │   │   │       └── UserRepository.cs
│   │   │   ├── Auth/
│   │   │   │   └── JwtTokenService.cs
│   │   │   └── DependencyInjection.cs
│   │   │
│   │   └── UserService.WebAPI/
│   │       ├── UserService.WebAPI.csproj
│   │       ├── Program.cs
│   │       ├── appsettings.json
│   │       ├── appsettings.Development.json
│   │       ├── Controllers/
│   │       │   ├── AuthController.cs
│   │       │   └── UsersController.cs
│   │       ├── Grpc/
│   │       │   └── UserRpcService.cs               ← Implements UserRpc gRPC service
│   │       ├── Middleware/
│   │       │   └── ValidationExceptionMiddleware.cs
│   │       └── Dockerfile
│   │
│   ├── DeviceService/
│   │   ├── DeviceService.Domain/
│   │   │   ├── DeviceService.Domain.csproj
│   │   │   ├── Entities/
│   │   │   │   └── Device.cs
│   │   │   ├── Enums/
│   │   │   │   └── DeviceStatus.cs
│   │   │   └── Interfaces/
│   │   │       └── IDeviceRepository.cs
│   │   │
│   │   ├── DeviceService.Application/
│   │   │   ├── DeviceService.Application.csproj
│   │   │   ├── Commands/
│   │   │   │   ├── RegisterDevice/
│   │   │   │   ├── AssignMonitor/
│   │   │   │   └── UpdateDeviceStatus/
│   │   │   └── Queries/
│   │   │       └── GetDevicesByMonitor/
│   │   │
│   │   ├── DeviceService.Infrastructure/
│   │   │   ├── DeviceService.Infrastructure.csproj
│   │   │   ├── Persistence/
│   │   │   │   ├── DeviceDbContext.cs
│   │   │   │   ├── Configurations/
│   │   │   │   │   └── DeviceConfiguration.cs
│   │   │   │   ├── Migrations/
│   │   │   │   └── Repositories/
│   │   │   │       └── DeviceRepository.cs
│   │   │   ├── Cache/
│   │   │   │   └── DeviceCacheService.cs          ← Write-Through Redis operations
│   │   │   ├── BackgroundServices/
│   │   │   │   └── HeartbeatExpiryWatcher.cs      ← Redis keyspace expiry subscriber
│   │   │   └── DependencyInjection.cs
│   │   │
│   │   └── DeviceService.WebAPI/
│   │       ├── DeviceService.WebAPI.csproj
│   │       ├── Program.cs
│   │       ├── appsettings.json
│   │       ├── Controllers/
│   │       │   └── DevicesController.cs
│   │       ├── Grpc/
│   │       │   └── DeviceRpcService.cs            ← Implements DeviceRpc gRPC service
│   │       └── Dockerfile
│   │
│   ├── IngestionService/
│   │   ├── IngestionService.Domain/
│   │   │   ├── IngestionService.Domain.csproj
│   │   │   └── Models/
│   │   │       └── TelemetryPacket.cs
│   │   │
│   │   ├── IngestionService.Application/
│   │   │   ├── IngestionService.Application.csproj
│   │   │   └── Pipeline/
│   │   │       └── TelemetryPipelineHandler.cs    ← Orchestrates the Hot Path steps
│   │   │
│   │   ├── IngestionService.Infrastructure/
│   │   │   ├── IngestionService.Infrastructure.csproj
│   │   │   ├── Auth/
│   │   │   │   └── DeviceApiKeyValidator.cs       ← Redis Hash lookup
│   │   │   ├── Cache/
│   │   │   │   └── HeartbeatManager.cs            ← SET EX on device:heartbeat
│   │   │   ├── ClickHouse/
│   │   │   │   └── ClickHouseLogWriter.cs         ← Batched bulk insert
│   │   │   ├── Messaging/
│   │   │   │   └── RabbitMqPublisher.cs           ← Publish to uav.events exchange
│   │   │   ├── Grpc/
│   │   │   │   └── DeviceServiceGrpcClient.cs     ← gRPC client to DeviceService
│   │   │   └── DependencyInjection.cs
│   │   │
│   │   └── IngestionService.WebAPI/
│   │       ├── IngestionService.WebAPI.csproj
│   │       ├── Program.cs
│   │       ├── appsettings.json
│   │       ├── Controllers/
│   │       │   └── IngestController.cs            ← POST /ingest endpoint
│   │       ├── BackgroundServices/
│   │       │   └── ClickHouseBatchFlusher.cs      ← Timed batch consumer from Channel<T>
│   │       └── Dockerfile
│   │
│   ├── LogService/
│   │   ├── LogService.Domain/
│   │   │   ├── LogService.Domain.csproj
│   │   │   └── Models/
│   │   │       └── DroneLog.cs
│   │   │
│   │   ├── LogService.Application/
│   │   │   ├── LogService.Application.csproj
│   │   │   └── Queries/
│   │   │       ├── GetLogsByDevice/
│   │   │       │   ├── GetLogsByDeviceQuery.cs
│   │   │       │   └── GetLogsByDeviceQueryHandler.cs
│   │   │       └── GetDetectionStats/
│   │   │           ├── GetDetectionStatsQuery.cs
│   │   │           └── GetDetectionStatsQueryHandler.cs
│   │   │
│   │   ├── LogService.Infrastructure/
│   │   │   ├── LogService.Infrastructure.csproj
│   │   │   ├── ClickHouse/
│   │   │   │   └── ClickHouseLogReader.cs
│   │   │   └── DependencyInjection.cs
│   │   │
│   │   └── LogService.WebAPI/
│   │       ├── LogService.WebAPI.csproj
│   │       ├── Program.cs
│   │       ├── appsettings.json
│   │       ├── Controllers/
│   │       │   └── LogsController.cs
│   │       └── Dockerfile
│   │
│   └── AlertService/
│       ├── AlertService.Domain/
│       │   ├── AlertService.Domain.csproj
│       │   └── Models/
│       │       └── AlertMessage.cs
│       │
│       ├── AlertService.Application/
│       │   ├── AlertService.Application.csproj
│       │   └── Handlers/
│       │       └── DroneDetectionAlertHandler.cs  ← Dispatches to SignalR hub
│       │
│       ├── AlertService.Infrastructure/
│       │   ├── AlertService.Infrastructure.csproj
│       │   ├── Messaging/
│       │   │   └── RabbitMqConsumer.cs            ← Consumes q.alert.realtime
│       │   ├── Grpc/
│       │   │   └── UserServiceGrpcClient.cs       ← Resolves monitor connection groups
│       │   └── DependencyInjection.cs
│       │
│       └── AlertService.WebAPI/
│           ├── AlertService.WebAPI.csproj
│           ├── Program.cs
│           ├── appsettings.json
│           ├── Hubs/
│           │   └── AlertHub.cs                    ← SignalR Hub
│           ├── BackgroundServices/
│           │   └── AlertConsumerWorker.cs         ← Hosted service wrapping RabbitMqConsumer
│           └── Dockerfile
```

---

## 6. STEP-BY-STEP BOILERPLATE GENERATION PLAN

Follow these phases **in strict order**. Complete each phase fully before starting the next. Use `dotnet CLI` commands exactly as specified.

---

### Phase 1: Repository Scaffolding & Infrastructure Configuration

**Goal**: Create the monorepo skeleton, solution file, Docker infrastructure, and Traefik configuration.

**Step 1.1 — Create directory structure**

```bash
mkdir uav-drone-detection && cd uav-drone-detection
mkdir -p src/Shared src/UserService src/DeviceService src/IngestionService src/LogService src/AlertService
mkdir -p traefik/dynamic infra/clickhouse infra/postgres infra/rabbitmq
```

**Step 1.2 — Create global solution file**

```bash
dotnet new sln -n uav-drone-detection
```

**Step 1.3 — Create `traefik/traefik.yml`**

```yaml
api:
  dashboard: true
  insecure: true # Dev only

entryPoints:
  web:
    address: ":80"

providers:
  docker:
    endpoint: "unix:///var/run/docker.sock"
    exposedByDefault: false
    network: uav_network
  file:
    directory: /etc/traefik/dynamic
    watch: true

log:
  level: INFO
```

**Step 1.4 — Create `traefik/dynamic/middlewares.yml`** (see Section 2.2 above for content)

**Step 1.5 — Create `infra/clickhouse/init.sql`** (see Section 4.2 for DDL)

**Step 1.6 — Create `infra/rabbitmq/definitions.json`**

```json
{
  "exchanges": [
    {
      "name": "uav.events",
      "vhost": "/",
      "type": "topic",
      "durable": true,
      "auto_delete": false
    }
  ],
  "queues": [
    {
      "name": "q.alert.realtime",
      "vhost": "/",
      "durable": true,
      "arguments": {}
    },
    {
      "name": "q.telemetry.analytics",
      "vhost": "/",
      "durable": true,
      "arguments": { "x-queue-mode": "lazy" }
    }
  ],
  "bindings": [
    {
      "source": "uav.events",
      "vhost": "/",
      "destination": "q.alert.realtime",
      "routing_key": "device.*.detection.critical"
    },
    {
      "source": "uav.events",
      "vhost": "/",
      "destination": "q.telemetry.analytics",
      "routing_key": "device.#"
    }
  ]
}
```

**Step 1.7 — Create `.env.example`**

```env
POSTGRES_USER=uav_admin
POSTGRES_PASSWORD=changeme
POSTGRES_DB=uav_db
REDIS_PASSWORD=changeme
RABBITMQ_DEFAULT_USER=guest
RABBITMQ_DEFAULT_PASS=guest
JWT_SECRET=replace-with-256bit-secret-in-production
CLICKHOUSE_USER=default
CLICKHOUSE_PASSWORD=
```

**Step 1.8 — Create `docker-compose.yml`**

```yaml
version: "3.9"

networks:
  uav_network:
    driver: bridge

services:
  traefik:
    image: traefik:v3.0
    ports:
      - "80:80"
      - "8090:8080" # Traefik dashboard (dev only)
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
      - ./traefik/traefik.yml:/etc/traefik/traefik.yml:ro
      - ./traefik/dynamic:/etc/traefik/dynamic:ro
    networks: [uav_network]

  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: ${POSTGRES_USER}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_DB: ${POSTGRES_DB}
    volumes:
      - postgres_data:/var/lib/postgresql/data
    networks: [uav_network]

  redis:
    image: redis:7-alpine
    command: >
      redis-server
      --requirepass ${REDIS_PASSWORD}
      --notify-keyspace-events Ex
    networks: [uav_network]

  rabbitmq:
    image: rabbitmq:3.13-management-alpine
    environment:
      RABBITMQ_DEFAULT_USER: ${RABBITMQ_DEFAULT_USER}
      RABBITMQ_DEFAULT_PASS: ${RABBITMQ_DEFAULT_PASS}
      RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS: '-rabbitmq_management load_definitions "/etc/rabbitmq/definitions.json"'
    volumes:
      - ./infra/rabbitmq/definitions.json:/etc/rabbitmq/definitions.json:ro
    networks: [uav_network]

  clickhouse:
    image: clickhouse/clickhouse-server:24-alpine
    volumes:
      - ./infra/clickhouse/init.sql:/docker-entrypoint-initdb.d/init.sql:ro
      - clickhouse_data:/var/lib/clickhouse
    networks: [uav_network]

  user-service:
    build:
      context: .
      dockerfile: src/UserService/UserService.WebAPI/Dockerfile
    environment:
      Postgres__ConnectionString: "Host=postgres;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
      Jwt__Secret: ${JWT_SECRET}
    labels:
      - "traefik.enable=true"
      - "traefik.http.routers.user-svc.rule=PathPrefix(`/api/v1/auth`) || PathPrefix(`/api/v1/users`)"
      - "traefik.http.routers.user-svc.entrypoints=web"
      - "traefik.http.services.user-svc.loadbalancer.server.port=8080"
      - "traefik.http.middlewares.strip-user.stripprefix.prefixes=/api/v1/auth,/api/v1/users"
      - "traefik.http.routers.user-svc.middlewares=strip-user@docker,cors-mw@file"
    networks: [uav_network]
    depends_on: [postgres]

  device-service:
    build:
      context: .
      dockerfile: src/DeviceService/DeviceService.WebAPI/Dockerfile
    environment:
      Postgres__ConnectionString: "Host=postgres;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
      Redis__ConnectionString: "redis,password=${REDIS_PASSWORD}"
    labels:
      - "traefik.enable=true"
      - "traefik.http.routers.device-svc.rule=PathPrefix(`/api/v1/devices`)"
      - "traefik.http.routers.device-svc.entrypoints=web"
      - "traefik.http.services.device-svc.loadbalancer.server.port=8080"
      - "traefik.http.middlewares.strip-device.stripprefix.prefixes=/api/v1/devices"
      - "traefik.http.routers.device-svc.middlewares=strip-device@docker,cors-mw@file"
    networks: [uav_network]
    depends_on: [postgres, redis]

  ingestion-service:
    build:
      context: .
      dockerfile: src/IngestionService/IngestionService.WebAPI/Dockerfile
    environment:
      Redis__ConnectionString: "redis,password=${REDIS_PASSWORD}"
      ClickHouse__ConnectionString: "Host=clickhouse;Port=8123;Username=${CLICKHOUSE_USER};Password=${CLICKHOUSE_PASSWORD}"
      RabbitMq__Host: rabbitmq
      RabbitMq__Username: ${RABBITMQ_DEFAULT_USER}
      RabbitMq__Password: ${RABBITMQ_DEFAULT_PASS}
      DeviceService__GrpcEndpoint: "http://device-service:8081"
    labels:
      - "traefik.enable=true"
      - "traefik.http.routers.ingest-svc.rule=PathPrefix(`/api/v1/ingest`)"
      - "traefik.http.routers.ingest-svc.entrypoints=web"
      - "traefik.http.services.ingest-svc.loadbalancer.server.port=8080"
      - "traefik.http.middlewares.strip-ingest.stripprefix.prefixes=/api/v1/ingest"
      - "traefik.http.routers.ingest-svc.middlewares=strip-ingest@docker,cors-mw@file"
    networks: [uav_network]
    depends_on: [redis, clickhouse, rabbitmq]

  log-service:
    build:
      context: .
      dockerfile: src/LogService/LogService.WebAPI/Dockerfile
    environment:
      ClickHouse__ConnectionString: "Host=clickhouse;Port=8123;Username=${CLICKHOUSE_USER};Password=${CLICKHOUSE_PASSWORD}"
    labels:
      - "traefik.enable=true"
      - "traefik.http.routers.log-svc.rule=PathPrefix(`/api/v1/logs`)"
      - "traefik.http.routers.log-svc.entrypoints=web"
      - "traefik.http.services.log-svc.loadbalancer.server.port=8080"
      - "traefik.http.middlewares.strip-log.stripprefix.prefixes=/api/v1/logs"
      - "traefik.http.routers.log-svc.middlewares=strip-log@docker,cors-mw@file"
    networks: [uav_network]
    depends_on: [clickhouse]

  alert-service:
    build:
      context: .
      dockerfile: src/AlertService/AlertService.WebAPI/Dockerfile
    environment:
      RabbitMq__Host: rabbitmq
      RabbitMq__Username: ${RABBITMQ_DEFAULT_USER}
      RabbitMq__Password: ${RABBITMQ_DEFAULT_PASS}
      UserService__GrpcEndpoint: "http://user-service:8081"
    labels:
      - "traefik.enable=true"
      - "traefik.http.routers.alert-svc.rule=PathPrefix(`/hubs/alerts`)"
      - "traefik.http.routers.alert-svc.entrypoints=web"
      - "traefik.http.services.alert-svc.loadbalancer.server.port=8080"
    networks: [uav_network]
    depends_on: [rabbitmq]

volumes:
  postgres_data:
  clickhouse_data:
```

---

### Phase 2: Shared Libraries

**Goal**: Create all shared Class Libraries and add them to the solution.

**Step 2.1 — Shared.Contracts (gRPC Proto)**

```bash
cd src/Shared
dotnet new classlib -n Shared.Contracts -f net9.0
cd Shared.Contracts
dotnet add package Google.Protobuf
dotnet add package Grpc.Tools
dotnet add package Grpc.Net.Client
```

Edit `Shared.Contracts.csproj` to include proto compilation:

```xml
<ItemGroup>
  <Protobuf Include="Protos\*.proto" GrpcServices="Both" />
</ItemGroup>
```

Create `Protos/device_service.proto` and `Protos/user_service.proto` as per Section 2.3.

**Step 2.2 — Shared.Events**

```bash
cd src/Shared
dotnet new classlib -n Shared.Events -f net9.0
```

Create `DroneDetectionEvent.cs` as per Section 4.4.

**Step 2.3 — Add to solution**

```bash
# From repo root
dotnet sln add src/Shared/Shared.Contracts/Shared.Contracts.csproj
dotnet sln add src/Shared/Shared.Events/Shared.Events.csproj
```

---

### Phase 3: UserService

**Step 3.1 — Scaffold 4 projects**

```bash
dotnet new classlib -n UserService.Domain -o src/UserService/UserService.Domain -f net9.0
dotnet new classlib -n UserService.Application -o src/UserService/UserService.Application -f net9.0
dotnet new classlib -n UserService.Infrastructure -o src/UserService/UserService.Infrastructure -f net9.0
dotnet new webapi -n UserService.WebAPI -o src/UserService/UserService.WebAPI -f net9.0 --no-openapi false
```

**Step 3.2 — Add project references (following dependency rule)**

```bash
cd src/UserService/UserService.Application && dotnet add reference ../UserService.Domain/UserService.Domain.csproj
cd src/UserService/UserService.Infrastructure && dotnet add reference ../UserService.Application/UserService.Application.csproj
cd src/UserService/UserService.WebAPI && dotnet add reference ../UserService.Infrastructure/UserService.Infrastructure.csproj
cd src/UserService/UserService.WebAPI && dotnet add reference ../../Shared/Shared.Contracts/Shared.Contracts.csproj
```

**Step 3.3 — Add NuGet packages**

```bash
# Application
cd src/UserService/UserService.Application
dotnet add package MediatR --version 12.*
dotnet add package FluentValidation.DependencyInjectionExtensions --version 11.*

# Infrastructure
cd src/UserService/UserService.Infrastructure
dotnet add package Microsoft.EntityFrameworkCore --version 9.*
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 9.*
dotnet add package Microsoft.AspNetCore.Identity.Core --version 9.*

# WebAPI
cd src/UserService/UserService.WebAPI
dotnet add package Grpc.AspNetCore --version 2.*
dotnet add package Serilog.AspNetCore --version 8.*
dotnet add package Swashbuckle.AspNetCore --version 7.*
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 9.*
```

**Step 3.4 — Add to solution**

```bash
dotnet sln add src/UserService/UserService.Domain/UserService.Domain.csproj
dotnet sln add src/UserService/UserService.Application/UserService.Application.csproj
dotnet sln add src/UserService/UserService.Infrastructure/UserService.Infrastructure.csproj
dotnet sln add src/UserService/UserService.WebAPI/UserService.WebAPI.csproj
```

**Step 3.5 — Create Dockerfile at `src/UserService/UserService.WebAPI/Dockerfile`**

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/UserService/UserService.WebAPI/UserService.WebAPI.csproj", "UserService/UserService.WebAPI/"]
COPY ["src/UserService/UserService.Infrastructure/UserService.Infrastructure.csproj", "UserService/UserService.Infrastructure/"]
COPY ["src/UserService/UserService.Application/UserService.Application.csproj", "UserService/UserService.Application/"]
COPY ["src/UserService/UserService.Domain/UserService.Domain.csproj", "UserService/UserService.Domain/"]
COPY ["src/Shared/Shared.Contracts/Shared.Contracts.csproj", "Shared/Shared.Contracts/"]
RUN dotnet restore "UserService/UserService.WebAPI/UserService.WebAPI.csproj"
COPY src/ .
WORKDIR "/src/UserService/UserService.WebAPI"
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "UserService.WebAPI.dll"]
```

**Step 3.6 — Implement core Program.cs structure**

```csharp
// src/UserService/UserService.WebAPI/Program.cs
using Serilog;
using UserService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddGrpc();
builder.Services.AddInfrastructure(builder.Configuration);  // Extension method in Infrastructure

// JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => { /* configure from JwtOptions */ });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", p => p.RequireRole("ADMIN"));
    options.AddPolicy("MonitorOrAdmin", p => p.RequireRole("MONITOR", "ADMIN"));
});

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration["Postgres:ConnectionString"]!);

var app = builder.Build();

// Apply EF Core migrations at startup (dev only)
await app.Services.ApplyMigrationsAsync();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGrpcService<UserRpcService>();
app.MapHealthChecks("/health");
app.Run();
```

---

### Phase 4: DeviceService

Repeat the same 6-step pattern as Phase 3. Key differences:

- Add packages: `StackExchange.Redis` to Infrastructure.
- `DeviceService.Infrastructure` contains `DeviceCacheService.cs` (Write-Through Redis) and `HeartbeatExpiryWatcher.cs` (BackgroundService subscribing to Redis keyspace events).
- gRPC: implement `DeviceRpcService.cs` hosting the `DeviceRpc` proto service.
- Controllers: `DevicesController` — enforces `[Authorize(Policy = "AdminOnly")]` on mutation endpoints; `[Authorize(Policy = "MonitorOrAdmin")]` on read endpoints.

**HeartbeatExpiryWatcher skeleton**:

```csharp
public class HeartbeatExpiryWatcher : BackgroundService
{
    // Subscribe to Redis channel: __keyevent@0__:expired
    // On message: parse device_id from "device:heartbeat:{device_id}"
    // Call DeviceRpc.UpdateDeviceStatus via IDeviceRepository or MediatR command
    // Publish device.{id}.status.changed to RabbitMQ via injected IRabbitMqPublisher
}
```

---

### Phase 5: IngestionService

Key implementation details:

**Step 5.1 — Scaffold as Worker + WebAPI hybrid**

```bash
dotnet new webapi -n IngestionService.WebAPI -o src/IngestionService/IngestionService.WebAPI -f net9.0
```

**Step 5.2 — Hot Path Pipeline** (implement in this exact order in `IngestController.cs`):

```csharp
[HttpPost]
public async Task<IActionResult> Ingest([FromBody] TelemetryPacketDto packet)
{
    // Step 1: Extract and validate X-Device-API-Key header
    var apiKey = Request.Headers["X-Device-API-Key"].FirstOrDefault();
    if (string.IsNullOrEmpty(apiKey)) return Unauthorized();

    // Step 2: Redis Hash lookup — device:meta:{device_id}
    var deviceMeta = await _apiKeyValidator.ValidateAsync(packet.DeviceId, apiKey);
    if (deviceMeta is null) return Unauthorized();

    // Step 3: Heartbeat check — SET EX device:heartbeat:{device_id} 600 "active"
    var statusChanged = await _heartbeatManager.RefreshAsync(packet.DeviceId, deviceMeta.Status);

    // Step 4: If status transition detected — fire-and-forget gRPC to DeviceService
    if (statusChanged)
        _ = Task.Run(() => _deviceGrpcClient.UpdateDeviceStatusAsync(packet.DeviceId, DeviceStatus.ONLINE));

    // Step 5: Enqueue to Channel<TelemetryPacket> for batched ClickHouse write
    await _logChannel.Writer.WriteAsync(packet.ToEntity());

    // Step 6: Update Redis Hash — device:latest_log:{device_id}
    await _cacheService.UpdateLatestLogAsync(packet.DeviceId, packet);

    // Step 7: If detected — publish to RabbitMQ
    if (packet.Detected)
        await _publisher.PublishAsync($"device.{packet.DeviceId}.detection.critical", packet.ToDetectionEvent());

    return Accepted();
}
```

**Step 5.3 — ClickHouse Batch Flusher** (`ClickHouseBatchFlusher.cs` as `BackgroundService`):

- Reads from `Channel<TelemetryPacket>` with a 500ms timeout.
- Collects up to 100 records.
- Flushes via `ClickHouseLogWriter.BulkInsertAsync(batch)`.

---

### Phase 6: LogService

Key implementation details:

- Pure CQRS Read service. No EF Core. No write operations.
- All queries go through `IClickHouseLogReader` implemented in Infrastructure.
- Endpoints require `[Authorize(Policy = "MonitorOrAdmin")]`.
- Query parameters must always include `device_id`. Validate that the authenticated user (from JWT `X-User-Id` claim) has access to the requested `device_id` by checking `monitor:devices:{user_id}` Redis Set.

**Example query handler pattern**:

```csharp
public class GetLogsByDeviceQueryHandler : IRequestHandler<GetLogsByDeviceQuery, IEnumerable<DroneLogDto>>
{
    // 1. Check authorization: verify device_id is in monitor:devices:{userId} Redis Set
    // 2. Execute parameterized ClickHouse query
    // 3. Map and return DTOs
}
```

---

### Phase 7: AlertService

Key implementation details:

- **No HTTP endpoints for data** — the only HTTP surface is the SignalR `/hubs/alerts` WebSocket endpoint.
- `AlertConsumerWorker` (BackgroundService): wraps `RabbitMqConsumer`, calls `AlertHub` methods on receipt.
- `AlertHub.cs`: Groups clients by `monitor_id`. On connection, add to group `$"monitor-{userId}"`.
- Manual ACK flow: `BasicAck` only after `_hubContext.Clients.Group(...).SendAsync(...)` completes.

**SignalR Hub skeleton**:

```csharp
public class AlertHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        // Extract userId from JWT (passed as query string ?access_token=... for WebSocket)
        var userId = Context.User?.FindFirst("sub")?.Value;
        if (userId is not null)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"monitor-{userId}");
        await base.OnConnectedAsync();
    }
}
```

**Worker push pattern**:

```csharp
// In AlertConsumerWorker
await _hubContext.Clients
    .Group($"monitor-{monitorId}")
    .SendAsync("DroneDetected", alertPayload, cancellationToken);
channel.BasicAck(ea.DeliveryTag, false);
```

---

### Phase 8: Final Integration & Validation Checklist

Before declaring the scaffolding complete, verify:

- [ ] All `.csproj` files reference correct target framework `net9.0`
- [ ] All Dockerfiles use multi-stage builds with `mcr.microsoft.com/dotnet/sdk:9.0` builder and `mcr.microsoft.com/dotnet/aspnet:9.0` runtime
- [ ] `docker-compose.yml` references all 5 service Dockerfiles with correct build contexts
- [ ] Traefik dashboard accessible at `http://localhost:8090` after `docker compose up`
- [ ] `dotnet build uav-drone-detection.sln` succeeds with zero errors
- [ ] Each service's `/health` endpoint returns 200 after `docker compose up`
- [ ] RabbitMQ Management UI (`http://localhost:15672`) shows `uav.events` exchange and both queues
- [ ] ClickHouse `drone_logs` table exists after container startup
- [ ] Redis `notify-keyspace-events` mode is `Ex` (verify with `redis-cli CONFIG GET notify-keyspace-events`)
- [ ] No service communicates via direct REST HTTP to another service (gRPC or RabbitMQ only)
- [ ] `appsettings.json` contains no hardcoded secrets — all via environment variable overrides

---

## APPENDIX A: Naming Conventions (Enforced)

| Artifact              | Convention                     | Example                        |
| --------------------- | ------------------------------ | ------------------------------ |
| C# Classes            | PascalCase                     | `DeviceCacheService`           |
| Interfaces            | `I` prefix + PascalCase        | `IDeviceRepository`            |
| Commands              | Noun + `Command` suffix        | `RegisterUserCommand`          |
| Queries               | Noun + `Query` suffix          | `GetLogsByDeviceQuery`         |
| Handlers              | Command/Query name + `Handler` | `RegisterUserCommandHandler`   |
| DTOs                  | Noun + `Dto` suffix            | `DroneLogDto`                  |
| Redis Keys            | `snake:case:{param}`           | `device:meta:{device_id}`      |
| RabbitMQ Routing Keys | `noun.{id}.verb.severity`      | `device.42.detection.critical` |
| Docker Service Names  | `kebab-case`                   | `ingestion-service`            |
| gRPC Service Names    | PascalCase + `Rpc` suffix      | `DeviceRpc`                    |

## APPENDIX B: Forbidden Patterns

> The AI assistant **must never** generate code using these patterns in this codebase.

1. **No MassTransit** — use raw `RabbitMQ.Client` exclusively.
2. **No Mediator pattern via HTTP** — services never call each other's REST APIs internally.
3. **No Entity Framework Core in IngestionService, LogService, or AlertService** — these services do not touch Postgres.
4. **No synchronous Redis/ClickHouse calls in the Ingestion hot path** — all I/O must be `async/await`.
5. **No `Thread.Sleep`** — use `Task.Delay` or `Channel<T>` timeout patterns.
6. **No inline connection string literals** — always use `IOptions<T>` with environment variable injection.
7. **No shared DbContext between UserService and DeviceService** — each service owns exactly one DbContext.
8. **No `.Result` or `.Wait()` on Tasks** — always `await`.
9. **No `dynamic` types** — all payloads must be strongly typed C# records or classes.
10. **No `Console.WriteLine`** — use `ILogger<T>` exclusively.
