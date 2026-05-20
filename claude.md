# claude.md — UAV Drone Detection System: AI Coding Assistant Source of Truth

> **STRICT INSTRUCTION FOR AI ASSISTANT:** This document is the **absolute source of truth** for all code generation, scaffolding, and architectural decisions in this repository. Never deviate from the conventions, patterns, naming rules, or technology choices defined here. Do not substitute libraries, invent new abstractions, or shift paradigms unless explicitly directed by the human operator in a follow-up prompt that explicitly overrides a specific section of this file.

---

## 1. PROJECT OVERVIEW

### Purpose

This is the backend for a **real-time AIoT UAV (Drone) Detection System**. Physical radar hardware — Software-Defined Radio (SDR) edge devices running RF signal classification — streams telemetry to this backend. The backend processes, stores, correlates, and alerts on those signals in near-real-time.

### Core Business Logic Goals

1. **High-throughput RF telemetry ingestion** — Accept a continuous stream of classification results from edge radar devices (SDR hardware running ML inference), targeting sub-50ms end-to-end latency from device POST to ClickHouse write.
2. **Device lifecycle management** — Register, assign, and monitor the health of physical radar assets. Detect hardware failures automatically via heartbeat expiry without requiring the device to self-report shutdown.
3. **Role-based access control** — Two roles exist: `ADMIN` (full CRUD) and `MONITOR` (read-only, scoped to assigned devices only). All resource access is enforced at the API Gateway and within each service.
4. **Real-time dashboard alerting** — When a drone is detected, push a WebSocket event to active supervisor dashboards within milliseconds of ingestion.
5. **Historical audit log** — Store every radar scan result (detection or clean sweep) in a columnar time-series store for paginated querying, filtering by device and time range.
6. **Edge device authentication** — Radar hardware authenticates with a pre-issued `X-Device-API-Key` header (not JWT), validated against Redis in O(1) without hitting PostgreSQL.

### Users

| Role      | Capabilities                                                              |
| --------- | ------------------------------------------------------------------------- |
| `ADMIN`   | Full CRUD on users and devices, assign monitors to devices, view all logs |
| `MONITOR` | Read-only; view only logs and device status for assigned devices          |

---

## 2. SYSTEM ARCHITECTURE & TRAEFIK API GATEWAY BOUNDARIES

### Microservice Inventory

| Service              | Project Name                 | Responsibility                                                                                                                             | Hosting Model                                         |
| -------------------- | ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------ | ----------------------------------------------------- |
| **IngestionService** | `UavSystem.IngestionService` | Accept edge device telemetry; validate against Redis; write to ClickHouse; trigger gRPC state updates; publish RabbitMQ events             | ASP.NET Core WebAPI + BackgroundService Worker        |
| **UserService**      | `UavSystem.UserService`      | User CRUD, JWT issuance and validation, role enforcement; gRPC server for auth queries from other services                                 | ASP.NET Core WebAPI + gRPC Server                     |
| **DeviceService**    | `UavSystem.DeviceService`    | Device CRUD, Redis cache sync on write, PostgreSQL source-of-truth; gRPC server for state transitions; RabbitMQ publisher on status change | ASP.NET Core WebAPI + gRPC Server                     |
| **LogService**       | `UavSystem.LogService`       | Paginated historical query API over ClickHouse; enforces monitor device-scoping                                                            | ASP.NET Core WebAPI                                   |
| **AlertService**     | `UavSystem.AlertService`     | Consumes RabbitMQ `q.alert.realtime`; broadcasts to WebSocket clients (SignalR Hub)                                                        | ASP.NET Core WebAPI + SignalR Hub + BackgroundService |

### Kong API Gateway Routing Matrix

- **Gateway Engine:** Kong Gateway (OSS v3.x run in DB-less/Declarative mode via `kong.yml`)
- **Proxy Port:** `:8000` (HTTP Ingress), `:8443` (HTTPS Ingress)
- All external HTTP routes are prefixed with `/api/v1/`.

```
External Client
      │
      ▼
Kong Gateway :8000      │
      │
      ├──► /api/v1/auth/**             ──► UserService        :8080 (HTTP)
      ├──► /api/v1/admin/users/**      ──► UserService        :8080
      ├──► /api/v1/devices/**          ──► DeviceService      :8081 (HTTP)
      ├──► /api/v1/admin/devices/**    ──► DeviceService      :8081
      ├──► /api/v1/logs/**             ──► LogService         :8082 (HTTP)
      ├──► /api/v1/telemetry/**        ──► IngestionService   :8083 (HTTP)
      └──► /ws/alerts/**               ──► AlertService       :8084 (WebSocket/SignalR)

Internal gRPC (NOT routed through Traefik — direct Docker DNS):
      IngestionService   ──► DeviceService   :9081 (gRPC)
      DeviceService      ──► UserService     :9080 (gRPC, role queries)
      API Gateway        ──► UserService     :9080 (gRPC, JWT ValidateToken on every request)
```

To protect your internal cluster while leaving paths like login and edge hardware telemetry exposed, Kong evaluates routes sequentially or matches specific nested route configs:

Public REST Endpoints: /api/v1/auth/login and /api/v1/telemetry/log bypass the forward-auth plugin entirely.

Protected REST Endpoints: All requests hitting /api/v1/devices/ or /api/v1/logs/ trigger a synchronous, binary gRPC call over HTTP/2 to the UserService on port 9080.

### Inter-Service Communication Paradigm

| Pattern                 | Used For                                                                          | Technology                              |
| ----------------------- | --------------------------------------------------------------------------------- | --------------------------------------- |
| **Synchronous gRPC**    | JWT validation, device status updates, role queries                               | gRPC (Grpc.AspNetCore, Grpc.Net.Client) |
| **Async Event-Driven**  | Drone detection alerts, device status-change fan-out                              | RabbitMQ (RabbitMQ.Client)              |
| **In-Memory Channel**   | IngestionService internal pipeline decoupling (HTTP response → background worker) | `System.Threading.Channels`             |
| **Cache-Aside (Redis)** | Device metadata validation, heartbeat, latest log snapshot                        | StackExchange.Redis                     |

---

## 3. TECH STACK & .NET ARCHITECTURAL CONVENTIONS

### Language & Runtime

- **Language:** C# 12
- **Runtime:** .NET 9
- **Global using directives:** Enabled per project. Add `GlobalUsings.cs` in each project root.
- **Nullable reference types:** Enabled globally (`<Nullable>enable</Nullable>`)

### Architectural Pattern: Clean Architecture per Service

Applied to: UserService and DeviceService. Enforces strict dependency separation across 4 explicit project boundaries.

```
UavSystem.<ServiceName>/
├── UavSystem.<ServiceName>.Domain/          # Entities, Enums, Interfaces, Domain Events (zero dependencies)
├── UavSystem.<ServiceName>.Application/     # Use Cases, CQRS Handlers (MediatR), DTOs, Interfaces
├── UavSystem.<ServiceName>.Infrastructure/  # EF Core DbContext, Redis, gRPC Clients, RabbitMQ, ClickHouse drivers
└── UavSystem.<ServiceName>.WebApi/          # ASP.NET Core host, Controllers, gRPC endpoints, DI wiring, Program.cs
```

**Dependency Rule:** `Domain` ← `Application` ← `Infrastructure` ← `WebApi`. No layer may reference a layer above it. `Domain` has **zero NuGet dependencies**.

### Architectural Pattern: High-Throughput Stream Services (Flat Pipeline / Worker Architecture)

Applied to: IngestionService and AlertService.
To minimize stack allocation overhead, crossing layers, and MediatR boilerplate during microsecond-critical routing, these are built as single, flat ASP.NET Core WebApi / Worker projects:

While AlertService structurally operates as the reactive "Command Side" of the system-level CQRS alerting matrix, its codebase is kept flat and unified (Consumers + Hubs directly wired) due to its purely transactional, stateless nature.

```
src/IngestionService/UavSystem.IngestionService.WebApi/
├── Controllers/ # TelemetryController (API Key check -> Channel.Writer -> HTTP 202)
├── Pipeline/ # IngestionWorker (BackgroundService thread pulling from Channel)
├── Clients/ # gRPC clients, ClickHouse bulk column writers
└── Models/ # Immutable structural records (LogPacket)
```

`IngestionService` is a high-throughput pipeline, **not** a CRUD service. It uses the **Transaction Script / Pipeline** pattern:

- No MediatR, no EF Core.
- Controller parses the payload, validates API Key against Redis, pushes to `System.Threading.Channels.Channel<LogPacket>`.
- `BackgroundService` (`IngestionWorker`) reads from the channel and orchestrates: Redis state diff → conditional gRPC call → ClickHouse write → conditional RabbitMQ publish.
- ClickHouse writes use the `ClickHouse.Client` NuGet package.

### CQRS Pattern ( LogService)

- Use **MediatR** for in-process CQRS.
- Commands (writes): `CreateUserCommand`, `RegisterDeviceCommand`, etc.
- Queries (reads): `GetDevicesByMonitorQuery`, `GetPaginatedLogsQuery`, etc.
- Handlers live in `Application/Features/<FeatureName>/`.

```
UavSystem.LogService/
├── UavSystem.LogService.Application/     # MediatR Query Handlers only (No Commands)
├── UavSystem.LogService.Infrastructure/  # Optimized ClickHouse.Client parameterized SQL commands
└── UavSystem.LogService.WebApi/          # REST Endpoint verifying Traefik role claims
```

### Required NuGet Packages (per project type)

**All WebApi projects:**

```xml
<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="9.*" />
<PackageReference Include="Swashbuckle.AspNetCore" Version="6.*" />
<PackageReference Include="Serilog.AspNetCore" Version="8.*" />
<PackageReference Include="Serilog.Sinks.Console" Version="5.*" />
```

**Application projects (Clean Architecture services):**

```xml
<PackageReference Include="MediatR" Version="12.*" />
<PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="11.*" />
<PackageReference Include="AutoMapper" Version="13.*" />
```

**Infrastructure projects:**

```xml
<!-- PostgreSQL -->
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.*" />
<!-- Redis -->
<PackageReference Include="StackExchange.Redis" Version="2.*" />
<!-- gRPC -->
<PackageReference Include="Grpc.AspNetCore" Version="2.*" />
<PackageReference Include="Grpc.Net.Client" Version="2.*" />
<PackageReference Include="Google.Protobuf" Version="3.*" />
<PackageReference Include="Grpc.Tools" Version="2.*" />
<!-- RabbitMQ -->
<PackageReference Include="RabbitMQ.Client" Version="6.*" />
<!-- JWT -->
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="9.*" />
<!-- ClickHouse (IngestionService and LogService) -->
<PackageReference Include="ClickHouse.Client" Version="7.*" />
```

### Code Style Conventions

- **Controllers:** Thin — only parse HTTP, call MediatR `ISender`, return `IActionResult`. No business logic.
- **Entities:** Immutable where possible. Use `private set` or `init`. No public parameterless constructors on domain entities.
- **Repository Pattern:** Define `IRepository<T>` in `Domain`. Implement in `Infrastructure`. Inject via DI.
- **Error Handling:** Use a global `ExceptionHandlerMiddleware` registered in `Program.cs`. Never return raw exceptions to clients.
- **Configuration:** All secrets via `IOptions<T>` pattern bound from `appsettings.json` / environment variables. Never hardcode secrets.
- **Async:** All I/O methods must be `async Task<T>`. Never use `.Result` or `.Wait()`.
- **Cancellation Tokens:** All async methods that accept `CancellationToken` must propagate it.

### Deployment: Docker Compose (Local Dev)

- Each service has its own `Dockerfile` using multi-stage builds (`build` → `publish` → `final` with `mcr.microsoft.com/dotnet/aspnet:9.0`).
- All services are on a shared Docker bridge network named `uav-network`.
- Traefik runs as the edge router, reads Docker labels for dynamic service discovery.
- Infrastructure (Postgres, ClickHouse, Redis, RabbitMQ) runs as named containers with health checks.

---

## 4. DATABASE & STORAGE STRATEGY

### PostgreSQL — User & Device Registry (UserService, DeviceService)

**Connection:** EF Core 9 with `Npgsql.EntityFrameworkCore.PostgreSQL`.
**DbContext per service:** `UserDbContext` in UserService.Infrastructure, `DeviceDbContext` in DeviceService.Infrastructure.
**Migrations:** Managed per service. Run via `dotnet ef migrations add` targeting the `Infrastructure` project.

**Schema — UserService:**

```sql
CREATE TYPE user_role AS ENUM ('ADMIN', 'MONITOR');

CREATE TABLE users (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        VARCHAR(100) NOT NULL,
    email       VARCHAR(150) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    role        user_role NOT NULL DEFAULT 'MONITOR',
    created_at  TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX idx_users_email ON users(email);
```

**C# Entity — User:**

```csharp
public sealed class User
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public UserRole Role { get; private set; } = UserRole.Monitor;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
public enum UserRole { Admin, Monitor }
```

**Schema — DeviceService:**

```sql
CREATE TYPE device_status AS ENUM ('ONLINE', 'OFFLINE', 'ERROR');

CREATE TABLE devices (
    device_id           BIGINT PRIMARY KEY,
    location_name       VARCHAR(255) NOT NULL,
    status              device_status NOT NULL DEFAULT 'OFFLINE',
    assigned_monitor_id UUID REFERENCES users(id) ON DELETE SET NULL,
    api_key_hash        VARCHAR(255) NOT NULL,   -- Hashed X-Device-API-Key
    updated_at          TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX idx_devices_monitor ON devices(assigned_monitor_id);
```

**C# Entity — Device:**

```csharp
public sealed class Device
{
    public long DeviceId { get; init; }
    public string LocationName { get; private set; } = null!;
    public DeviceStatus Status { get; private set; } = DeviceStatus.Offline;
    public Guid? AssignedMonitorId { get; private set; }
    public string ApiKeyHash { get; private set; } = null!;  // BCrypt hash
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;
}
public enum DeviceStatus { Online, Offline, Error }
```

### ClickHouse — Log Storage (IngestionService writes, LogService reads)

**Driver:** `ClickHouse.Client` NuGet package. No ORM — raw SQL via `ClickHouseConnection`.

**Schema:**

```sql
CREATE TABLE radar_logs (
    device_id    Int64,
    timeStamp    DateTime64(3, 'UTC'),
    location     String,
    status       LowCardinality(String),
    detected     UInt8,                       -- 0=false, 1=true
    drone_type   LowCardinality(Nullable(String)),
    controlState LowCardinality(Nullable(String)),
    accuracy     Float32
) ENGINE = MergeTree()
PARTITION BY toYYYYMM(timeStamp)
ORDER BY (device_id, timeStamp)
TTL timeStamp + INTERVAL 3 MONTH;
```

**Write pattern (IngestionService):**

```csharp
// Use bulk insert via ClickHouseCommand — never insert row-by-row
await using var cmd = connection.CreateCommand();
cmd.CommandText = "INSERT INTO radar_logs VALUES";
// Append via ClickHouseColumnWriter for batch inserts
```

**Read pattern (LogService):**

```csharp
// Parameterized queries only — never string interpolation
const string sql = @"
    SELECT device_id, timeStamp, status, detected, drone_type, controlState, accuracy
    FROM radar_logs
    WHERE device_id = {device_id:Int64}
      AND timeStamp BETWEEN {start:DateTime} AND {end:DateTime}
    ORDER BY timeStamp DESC
    LIMIT {limit:UInt32} OFFSET {offset:UInt32}";
```

### Redis — Distributed Cache (IngestionService, DeviceService)

**Driver:** `StackExchange.Redis`. Inject `IConnectionMultiplexer` as singleton.

**Key Namespace Design:**

| Key Pattern                     | Type   | Fields                                                            | TTL                               | Purpose                      |
| ------------------------------- | ------ | ----------------------------------------------------------------- | --------------------------------- | ---------------------------- |
| `device:meta:{device_id}`       | Hash   | `status`, `monitor_id`, `location`, `api_key_hash`                | None (persistent)                 | Ingestion packet validation  |
| `device:heartbeat:{device_id}`  | String | `"active"`                                                        | 10 minutes                        | Radar liveness detection     |
| `device:latest_log:{device_id}` | Hash   | `timestamp`, `detected`, `drone_type`, `accuracy`, `controlState` | None (overwritten on each packet) | Dashboard real-time snapshot |
| `monitor:devices:{user_id}`     | Set    | `[device_id, ...]`                                                | None (persistent)                 | Monitor → devices index      |

**Redis Keyspace Notifications:** Enable `notify-keyspace-events Ex` in `redis.conf` or via `CONFIG SET`. A `BackgroundService` in DeviceService subscribes to `__keyevent@0__:expired` to detect heartbeat expiry and trigger the OFFLINE state transition.

**Write-Through policy:** Every time DeviceService writes to PostgreSQL (register, update, reassign), it **immediately** writes the same data to Redis. This is non-negotiable — Redis must never serve stale device metadata to the IngestionService.

### RabbitMQ — Event Bus (IngestionService publishes, AlertService consumes)

**Driver:** `RabbitMQ.Client` (official AMQP client).

**Exchange:**

```
Name:    uav.events
Type:    topic
Durable: true
```

**Queues:**

| Queue                   | Binding Key                   | Consumer                    | Properties                              |
| ----------------------- | ----------------------------- | --------------------------- | --------------------------------------- |
| `q.alert.realtime`      | `device.*.detection.critical` | AlertService                | Durable, Auto-delete: false, Manual ACK |
| `q.telemetry.analytics` | `device.#`                    | (future analytics consumer) | Durable, `x-queue-mode: lazy`           |
| `q.status.changes`      | `device.*.status.changed`     | AlertService                | Durable, Manual ACK                     |
| `q.malformed.dlq`       | (Dead Letter Exchange target) | Manual inspection           | Durable                                 |

**Publisher Confirms:** Always enabled on the RabbitMQ channel in IngestionService. Never fire-and-forget to the broker.

**Manual ACK:** AlertService must call `BasicAck` only after successfully broadcasting over SignalR. On failure → `BasicNack` with `requeue: true`.

---

## 5. INITIAL REPOSITORY DIRECTORY TREE

```
uav-detection-system/
├── UavSystem.sln
├── docker-compose.yml
├── kong/
│   └── kong.yml                             ← Declarative routing config file
├── proto/
│   └── uav/
│       └── v1/
│           ├── user_service.proto           ← Generated to namespace UavSystem.Shared.Contracts.Grpc
│           ├── device_service.proto
│           └── common.proto
├── src/
│   ├── Shared/
│   │   ├── UavSystem.Shared.Contracts/      ← Enums, Shared Events, Generated gRPC code templates
│   │   └── UavSystem.Shared.Infrastructure/ ← Common logging, Redis key utilities, RabbitMQ bases
│   ├── UserService/                         ← Clean Architecture (4-Project Layering)
│   │   ├── UavSystem.UserService.Domain/
│   │   ├── UavSystem.UserService.Application/
│   │   ├── UavSystem.UserService.Infrastructure/
│   │   └── UavSystem.UserService.WebApi/
│   ├── DeviceService/                       ← Clean Architecture (4-Project Layering)
│   │   ├── UavSystem.DeviceService.Domain/
│   │   ├── UavSystem.DeviceService.Application/
│   │   ├── UavSystem.DeviceService.Infrastructure/
│   │   └── UavSystem.DeviceService.WebApi/
│   ├── IngestionService/                    ← Flat Worker Pipeline (1 Project)
│   │   └── UavSystem.IngestionService.WebApi/
│   ├── LogService/                          ← Lean Read-CQRS Optimization (3 Projects)
│   │   ├── UavSystem.LogService.Application/
│   │   ├── UavSystem.LogService.Infrastructure/
│   │   └── UavSystem.LogService.WebApi/
│   └── AlertService/                        ← Flat Event Worker (1 Project)
│       └── UavSystem.AlertService.WebApi/
```

---

## 6. STEP-BY-STEP BOILERPLATE GENERATION PLAN

Follow these phases **strictly in order**. Complete each phase before beginning the next. After each phase, validate that Docker Compose `up` builds without errors.

---

### Phase 1: Repository Root, Docker Compose & Traefik Setup

**Goal:** Establish the container orchestration layer and routing infrastructure before any service code exists.

**Steps:**

1. Create the root directory `uav-detection-system/`.
2. Create `docker-compose.yml` with the following named services, all on `uav-network`:
   - `kong` — image `kong:3.5-alpine`, mapped to ports `:80` (public HTTP proxy ingress) and `:8090` (Kong Manager UI inside dev environment). Mounts declarative config route trees.
   - `postgres` — image `postgres:16-alpine`, health check `pg_isready`, env from `.env`.
   - `clickhouse` — image `clickhouse/clickhouse-server:24`, expose `:8123` (HTTP) and `:9000` (native).
   - `redis` — image `redis:7-alpine`, command `redis-server --notify-keyspace-events Ex`.
   - `rabbitmq` — image `rabbitmq:3.13-management-alpine`, expose `:5672` and `:15672`.
   - Placeholder `userservice`, `deviceservice`, `ingestionservice`, `logservice`, `alertservice` stubs (build context pointing to future Dockerfiles, `restart: unless-stopped`).
3. Create `kong/kong.yml`:

   ```yaml
   _format_version: "3.0"
   _transform: true

   services:
     # --- USER SERVICE BOUNDARY ---
     - name: user-service
       url: http://userservice:8080
       routes:
         - name: public-auth-login
           paths:
             - /api/v1/auth/login
           strip_path: false # Passes full path to .NET controller

         - name: protected-admin-users
           paths:
             - /api/v1/admin/users
           strip_path: false
           plugins:
             - name: forward-auth
               config:
                 url: grpc://userservice:9080 # High-speed gRPC token check
                 auth_response_headers:
                   - X-User-ID
                   - X-User-Role

     # --- DEVICE SERVICE BOUNDARY ---
     - name: device-service
       url: http://deviceservice:8081
       plugins:
         - name: forward-auth
           config:
             url: grpc://userservice:9080 # Protects all device mutations/reads
             auth_response_headers:
               - X-User-ID
               - X-User-Role
       routes:
         - name: device-management-routes
           paths:
             - /api/v1/devices
             - /api/v1/admin/devices
           strip_path: false

     # --- LOG SERVICE BOUNDARY ---
     - name: log-service
       url: http://logservice:8082
       plugins:
         - name: forward-auth
           config:
             url: grpc://userservice:9080
             auth_response_headers:
               - X-User-ID
               - X-User-Role
       routes:
         - name: analytical-log-routes
           paths:
             - /api/v1/logs
           strip_path: false

     # --- INGESTION SERVICE BOUNDARY (EDGE TELEMETRY) ---
     - name: ingestion-service
       url: http://ingestionservice:8083
       routes:
         - name: public-edge-telemetry
           paths:
             - /api/v1/telemetry/log # Verified via X-Device-API-Key inside app code, NO JWT check
           strip_path: false

     # --- ALERT SERVICE BOUNDARY (WEBSOCKETS) ---
     - name: alert-service
       url: http://alertservice:8084
       routes:
         - name: alert-websocket-hub
           paths:
             - /ws/alerts # SignalR real-time stream channel
           strip_path: false
   ```

4. Create `.env.example` with all required variables (DB passwords, JWT secret, ClickHouse creds, RabbitMQ creds).

```env
 # PostgreSQL Properties
 POSTGRES_USER=uav_admin
 POSTGRES_PASSWORD=secure_dev_postgres_password_123
 POSTGRES_DB=uav_system

 # Redis Runtime Secrets
 REDIS_PASSWORD=secure_dev_redis_password_456

 # Broker Credentials
 RABBITMQ_DEFAULT_USER=uav_broker_admin
 RABBITMQ_DEFAULT_PASS=secure_dev_rabbitmq_password_789

 # Columnar Stores Configuration
 CLICKHOUSE_USER=default
 CLICKHOUSE_PASSWORD=secure_dev_clickhouse_password_000

 # Token Signing Authority
 JWT_SECRET=super_secret_32_character_signing_key_boundary_123!
```

6. Create `.gitignore` (include `.env`, `bin/`, `obj/`, `*.user`).

```gitignore
.env
bin/
obj/
*.user
*.suo
.idea/
.vscode/
```

---

### Phase 2: Shared Contracts & Proto Definitions

**Goal:** Define the shared language (events, enums, gRPC contracts) before any service implements them.

**Steps:**

1. Create `proto/uav/v1/user_service.proto`:

   ```protobuf
   syntax = "proto3";
   package uav.v1;
   option csharp_namespace = "UavSystem.Shared.Contracts.Grpc";

   service UserService {
     rpc ValidateToken (ValidateTokenRequest) returns (ValidateTokenResponse);
     rpc GetUserMonitoredDevices (GetDevicesRequest) returns (GetDevicesResponse);
   }
   message ValidateTokenRequest { string token = 1; }
   message ValidateTokenResponse { string user_id = 1; string role = 2; bool is_valid = 3; }
   message GetDevicesRequest { string user_id = 1; }
   message GetDevicesResponse { repeated int64 device_ids = 1; }
   ```

2. Create `proto/uav/v1/device_service.proto`:

   ```protobuf
   syntax = "proto3";
   package uav.v1;
   option csharp_namespace = "UavSystem.Shared.Contracts.Grpc";
   import "google/protobuf/timestamp.proto";

   service InternalDeviceService {
     rpc UpdateDeviceStatus (UpdateDeviceStatusRequest) returns (UpdateDeviceStatusResponse);
     rpc ReportStateChange (StateChangeRequest) returns (StateChangeResponse);
   }
   message UpdateDeviceStatusRequest { int64 device_id = 1; string new_status = 2; google.protobuf.Timestamp timestamp = 3; }
   message UpdateDeviceStatusResponse { bool success = 1; }
   message StateChangeRequest { int64 device_id = 1; string original_status = 2; string updated_status = 3; google.protobuf.Timestamp occurrence_time = 4; string error_details = 5; }
   message StateChangeResponse { bool database_committed = 1; bool event_broadcasted = 2; }
   ```

3. Run `dotnet new classlib -n UavSystem.Shared.Contracts -o src/Shared/UavSystem.Shared.Contracts --framework net9.0`.
4. Add `Enums/UserRole.cs`, `Enums/DeviceStatus.cs`, `Events/DroneDetectedEvent.cs`, `Events/DeviceStatusChangedEvent.cs`.
5. Run `dotnet new classlib -n UavSystem.Shared.Infrastructure -o src/Shared/UavSystem.Shared.Infrastructure --framework net9.0`.
6. Add `Messaging/RabbitMqPublisherBase.cs`, `Messaging/RabbitMqConsumerBase.cs`, `Caching/RedisKeys.cs`.
7. Create root `UavSystem.sln`: `dotnet new sln -n UavSystem`.
8. Add both shared projects to the root `.sln`.

**`RedisKeys.cs` pattern (use this format throughout all services):**

```csharp
public static class RedisKeys
{
    public static string DeviceMeta(long deviceId)      => $"device:meta:{deviceId}";
    public static string DeviceHeartbeat(long deviceId) => $"device:heartbeat:{deviceId}";
    public static string DeviceLatestLog(long deviceId) => $"device:latest_log:{deviceId}";
    public static string MonitorDevices(Guid userId)    => $"monitor:devices:{userId}";
}
```

---

### Phase 3: UserService Skeleton

**CLI commands (run from repo root):**

```bash
dotnet new classlib -n UavSystem.UserService.Domain         -o src/UserService/UavSystem.UserService.Domain         --framework net9.0
dotnet new classlib -n UavSystem.UserService.Application    -o src/UserService/UavSystem.UserService.Application    --framework net9.0
dotnet new classlib -n UavSystem.UserService.Infrastructure -o src/UserService/UavSystem.UserService.Infrastructure --framework net9.0
dotnet new webapi   -n UavSystem.UserService.WebApi         -o src/UserService/UavSystem.UserService.WebApi         --framework net9.0
dotnet new sln      -n UavSystem.UserService                -o src/UserService/
```

**Add projects to UserService.sln and root UavSystem.sln.**

**Key files to generate:**

- `Domain/Entities/User.cs` — as defined in Section 4.
- `Domain/Interfaces/IUserRepository.cs` — `Task<User?> GetByEmailAsync(string email, CancellationToken ct)`, `Task<IEnumerable<User>> GetAllAsync(CancellationToken ct)`, `Task AddAsync(User user, CancellationToken ct)`.
- `Infrastructure/Persistence/UserDbContext.cs` — inherits `DbContext`, has `DbSet<User> Users`.
- `Infrastructure/Auth/JwtService.cs` — implements `IJwtService`: `string GenerateToken(User user)` and `ClaimsPrincipal? ValidateToken(string token)`.
- `WebApi/Controllers/AuthController.cs` — `POST /auth/login` → calls `ISender.Send(new LoginCommand(...))`.
- `WebApi/Controllers/AdminUsersController.cs` — `POST /admin/users` → requires `X-User-Role: ADMIN` header check.
- `WebApi/GrpcServices/UserGrpcService.cs` — implements `UserService.UserServiceBase` from the proto.
- `WebApi/Middleware/ExceptionHandlerMiddleware.cs` — catches all exceptions, logs via Serilog, returns RFC 7807 Problem Details.
- `WebApi/Program.cs` — registers all DI, EF Core, gRPC, Auth, Swagger, Serilog, Traefik labels in Dockerfile.

**Traefik labels in UserService Dockerfile's docker-compose section:**

```yaml
labels:
  - "traefik.enable=true"
  - "traefik.http.routers.userservice.rule=PathPrefix(`/api/v1/auth`) || PathPrefix(`/api/v1/admin/users`)"
  - "traefik.http.routers.userservice.entrypoints=web"
  - "traefik.http.routers.userservice.middlewares=strip-api-v1"
  - "traefik.http.services.userservice.loadbalancer.server.port=8080"
```

---

### Phase 4: DeviceService Skeleton

**CLI commands:**

```bash
dotnet new classlib -n UavSystem.DeviceService.Domain         -o src/DeviceService/UavSystem.DeviceService.Domain         --framework net9.0
dotnet new classlib -n UavSystem.DeviceService.Application    -o src/DeviceService/UavSystem.DeviceService.Application    --framework net9.0
dotnet new classlib -n UavSystem.DeviceService.Infrastructure -o src/DeviceService/UavSystem.DeviceService.Infrastructure --framework net9.0
dotnet new webapi   -n UavSystem.DeviceService.WebApi         -o src/DeviceService/UavSystem.DeviceService.WebApi         --framework net9.0
```

**Key files to generate:**

- `Domain/Entities/Device.cs` — as defined in Section 4.
- `Infrastructure/Caching/RedisDeviceSyncService.cs` — Implements `IRedisDeviceSyncService`. Called by every Command handler after PostgreSQL write. Uses `IDatabase.HashSetAsync` and `IDatabase.SetAddAsync`.
- `Infrastructure/BackgroundServices/RedisHeartbeatWatcherService.cs`:
  ```csharp
  // Subscribes to: __keyevent@0__:expired
  // Pattern match: if key starts with "device:heartbeat:"
  // Extract device_id, call DeviceGrpcService.UpdateDeviceStatus(OFFLINE)
  // DeviceService then publishes device.{id}.status.changed to RabbitMQ
  ```
- `Infrastructure/Messaging/DeviceStatusPublisher.cs` — publishes to exchange `uav.events` with routing key `device.{deviceId}.status.changed`.
- `WebApi/GrpcServices/DeviceGrpcService.cs` — implements `InternalDeviceService.InternalDeviceServiceBase`.
- `WebApi/Controllers/DevicesController.cs` — `GET /devices`: reads from PostgreSQL (slave if configured), merges with Redis `device:latest_log:{id}` for each device ID in the result set.
- `WebApi/Controllers/AdminDevicesController.cs` — `POST /admin/devices`: generates API key, stores hash in Postgres + raw key synced to Redis `device:meta:{id}`.

---

### Phase 5: IngestionService Skeleton

**CLI commands:**

```bash
dotnet new webapi -n UavSystem.IngestionService.WebApi -o src/IngestionService/UavSystem.IngestionService.WebApi --framework net9.0
```

**Key files to generate:**

**`Pipeline/Models/LogPacket.cs`:**

```csharp
public sealed record LogPacket(
    long DeviceId,
    DateTime Timestamp,
    string Status,
    string Location,
    bool Detected,
    string DroneType,
    float Accuracy
);
```

**`Controllers/TelemetryController.cs`:**

```csharp
[HttpPost("telemetry/log")]
public async Task<IActionResult> ReceiveLog(
    [FromHeader(Name = "X-Device-API-Key")] string apiKey,
    [FromQuery] long device_id,
    [FromBody] TelemetryRequestDto body,
    CancellationToken ct)
{
    // 1. Validate API key against Redis device:meta:{device_id} hash field "api_key_hash"
    // 2. If invalid → return 401
    // 3. Map body to LogPacket
    // 4. await _channel.Writer.WriteAsync(packet, ct);
    // 5. return Accepted(new { status = "ACCEPTED", received_at = DateTime.UtcNow });
}
```

**`Pipeline/IngestionWorker.cs`:**

```csharp
// BackgroundService — ExecuteAsync reads from Channel<LogPacket>
// For each packet:
//   Step 1: DeviceValidationStep  — HGETALL device:meta:{id} from Redis
//   Step 2: StateComparisonStep   — compare packet.Status vs cached status
//     → If changed: UPDATE Redis + call DeviceGrpcClient.UpdateDeviceStatus()
//     → If unchanged: EXPIRE device:heartbeat:{id} 600s
//   Step 3: HMSET device:latest_log:{id} with packet fields
//   Step 4: ClickHouseWriteStep   — INSERT into radar_logs
//   Step 5: AlertPublishStep      — if packet.Detected == true, publish to RabbitMQ
//             routing key: device.{id}.detection.critical
//             payload: DroneDetectedEvent (from Shared.Contracts)
```

**`Program.cs` registrations:**

```csharp
builder.Services.AddSingleton(Channel.CreateUnbounded<LogPacket>());
builder.Services.AddSingleton(sp => sp.GetRequiredService<Channel<LogPacket>>().Reader);
builder.Services.AddSingleton(sp => sp.GetRequiredService<Channel<LogPacket>>().Writer);
builder.Services.AddHostedService<IngestionWorker>();
// Register Redis, gRPC client for DeviceService, ClickHouse connection, RabbitMQ connection
```

---

### Phase 6: LogService Skeleton

**CLI commands:**

```bash
dotnet new classlib -n UavSystem.LogService.Application    -o src/LogService/UavSystem.LogService.Application    --framework net9.0
dotnet new classlib -n UavSystem.LogService.Infrastructure -o src/LogService/UavSystem.LogService.Infrastructure --framework net9.0
dotnet new webapi   -n UavSystem.LogService.WebApi         -o src/LogService/UavSystem.LogService.WebApi         --framework net9.0
```

**Key files to generate:**

- `WebApi/Controllers/LogsController.cs`:

  ```
  GET /logs?device_id=1002&start_time=...&end_time=...&limit=50&page=1
  ```

  - Read `X-User-Role` and `X-User-ID` headers (injected by Traefik ForwardAuth).
  - If `MONITOR`: call UserService gRPC `GetUserMonitoredDevices` → validate `device_id` is in the returned set. Return `403` if not.
  - If `ADMIN`: no scoping.
  - Call `IClickHouseLogRepository.GetPaginatedAsync(...)`.
  - Return paginated response with `metadata` + `data` array.

- `Infrastructure/ClickHouse/ClickHouseLogRepository.cs` — implements `ILogRepository` using `ClickHouseConnection`. Use parameterized queries with `{param:Type}` syntax.

---

### Phase 7: AlertService Skeleton

**CLI commands:**

```bash
dotnet new webapi -n UavSystem.AlertService.WebApi -o src/AlertService/UavSystem.AlertService.WebApi --framework net9.0
```

**Key files to generate:**

- `Hubs/AlertHub.cs`:
  ```csharp
  public class AlertHub : Hub
  {
      // Clients connect to /ws/alerts
      // Groups by monitor user ID (from JWT claim passed as query param on connect)
      public override async Task OnConnectedAsync()
      {
          var userId = Context.GetHttpContext()?.Request.Query["userId"];
          if (userId.HasValue) await Groups.AddToGroupAsync(Context.ConnectionId, userId!);
          await base.OnConnectedAsync();
      }
  }
  ```
- `Consumers/DroneAlertConsumer.cs`:
  ```csharp
  // BackgroundService — subscribes to q.alert.realtime and q.status.changes
  // On DroneDetectedEvent:
  //   Deserialize payload
  //   Determine which monitor owns device_id (SMEMBERS monitor:devices:{userId} from Redis, or reverse lookup)
  //   hubContext.Clients.Group(monitorId).SendAsync("DroneDetected", payload)
  //   channel.BasicAck()
  // On failure: channel.BasicNack(requeue: true)
  ```
- `Program.cs`:
  ```csharp
  builder.Services.AddSignalR();
  builder.Services.AddHostedService<DroneAlertConsumer>();
  app.MapHub<AlertHub>("/ws/alerts");
  ```
- Traefik WebSocket label:
  ```yaml
  - "traefik.http.routers.alertservice.rule=PathPrefix(`/ws/alerts`)"
  ```

---

### Phase 8: Dockerfiles

**Generate a multi-stage `Dockerfile` for each service.** Template (adjust `ServiceName` and port):

```dockerfile
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy shared projects first (for layer caching)
COPY src/Shared/ src/Shared/
COPY src/UserService/ src/UserService/
COPY proto/ proto/

RUN dotnet restore src/UserService/UavSystem.UserService.WebApi/UavSystem.UserService.WebApi.csproj

WORKDIR /src/src/UserService/UavSystem.UserService.WebApi
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "UavSystem.UserService.WebApi.dll"]
```

---

### Phase 9: Database Migrations & ClickHouse Schema Init

1. Add EF Core migration for UserService:
   ```bash
   cd src/UserService/UavSystem.UserService.WebApi
   dotnet ef migrations add InitialCreate --project ../UavSystem.UserService.Infrastructure --startup-project .
   ```
2. Add EF Core migration for DeviceService.
3. Create `infra/clickhouse/init.sql` with the `CREATE TABLE radar_logs` DDL from Section 4. Mount this file into the ClickHouse container as an init script via Docker Compose volume.
4. Create `infra/postgres/init/` directory with role and database creation scripts if needed.

---

### Phase 10: Validation Checklist

Before declaring the scaffold complete, verify:

- [ ] `docker-compose up --build` succeeds with zero container crashes after 60 seconds.
- [ ] Traefik dashboard (`:8080`) shows all 5 services as healthy routers.
- [ ] `POST http://localhost/api/v1/auth/login` returns 200 with a JWT.
- [ ] `GET http://localhost/api/v1/devices` without JWT returns 401 (Traefik ForwardAuth blocks it).
- [ ] `POST http://localhost/api/v1/telemetry/log` with a valid `X-Device-API-Key` header returns 202.
- [ ] Redis `HGETALL device:meta:1002` returns metadata after a device is registered.
- [ ] RabbitMQ management UI (`:15672`) shows exchanges `uav.events` and queues `q.alert.realtime`, `q.status.changes`, `q.telemetry.analytics`.
- [ ] ClickHouse HTTP interface (`http://localhost:8123/ping`) returns `Ok.`
- [ ] Serilog outputs structured JSON logs in all services.
- [ ] All gRPC services are reachable on their internal ports via `grpcurl` or a test client.

---

## APPENDIX A: Environment Variables Reference

```dotenv
# PostgreSQL
POSTGRES_HOST=postgres
POSTGRES_PORT=5432
POSTGRES_DB=uav_system
POSTGRES_USER=uav_admin
POSTGRES_PASSWORD=<strong_password>

# ClickHouse
CLICKHOUSE_HOST=clickhouse
CLICKHOUSE_PORT=8123
CLICKHOUSE_DB=uav_logs
CLICKHOUSE_USER=default
CLICKHOUSE_PASSWORD=<strong_password>

# Redis
REDIS_HOST=redis
REDIS_PORT=6379

# RabbitMQ
RABBITMQ_HOST=rabbitmq
RABBITMQ_PORT=5672
RABBITMQ_USER=uav_admin
RABBITMQ_PASSWORD=<strong_password>
RABBITMQ_VHOST=/

# JWT
JWT_SECRET=<min_32_char_random_secret>
JWT_ISSUER=uav-detection-system
JWT_AUDIENCE=uav-supervisors
JWT_EXPIRES_SECONDS=86400

# Traefik
TRAEFIK_DASHBOARD_PORT=8080
```

---

## APPENDIX B: Naming Conventions

| Artifact                | Convention                   | Example                                |
| ----------------------- | ---------------------------- | -------------------------------------- |
| Solution/Project        | `PascalCase`                 | `UavSystem.UserService.WebApi`         |
| C# Classes              | `PascalCase`                 | `DeviceStatusPublisher`                |
| C# Interfaces           | `IPascalCase`                | `IDeviceRepository`                    |
| C# Methods              | `PascalCase`                 | `GetPaginatedLogsAsync`                |
| C# Parameters/Variables | `camelCase`                  | `deviceId`                             |
| EF Core Migrations      | `PascalCase`                 | `InitialCreate`, `AddApiKeyToDevices`  |
| Redis Keys              | `snake_case:colon_separated` | `device:meta:1002`                     |
| RabbitMQ Routing Keys   | `dot.separated.lower`        | `device.1002.detection.critical`       |
| RabbitMQ Queues         | `q.dot.separated.lower`      | `q.alert.realtime`                     |
| Docker Compose Services | `lowercase`                  | `userservice`, `deviceservice`         |
| REST Endpoints          | `kebab-case`                 | `/api/v1/admin/users`                  |
| gRPC Services           | `PascalCase` per proto       | `UserService`, `InternalDeviceService` |
| Proto Files             | `snake_case`                 | `user_service.proto`                   |
| Environment Variables   | `UPPER_SNAKE_CASE`           | `POSTGRES_PASSWORD`                    |

---

## APPENDIX C: Critical Architectural Rules (Never Violate)

1. **Never query PostgreSQL from IngestionService.** All device validation goes through Redis. If Redis says the device doesn't exist, reject the packet.
2. **Never store `latest_log` in PostgreSQL.** It lives in Redis `device:latest_log:{id}` only.
3. **Never use `async void`.** All async methods return `Task` or `Task<T>`.
4. **Never use `.Result` or `.Wait()` on Tasks.** This causes deadlocks in ASP.NET Core.
5. **Never hardcode connection strings or secrets in source code.** All configuration via `IOptions<T>` from environment variables.
6. **Always return `202 Accepted` from `POST /api/v1/telemetry/log`.** The device must not wait for processing to complete.
7. **Always use the Write-Through cache policy in DeviceService.** Every PostgreSQL write must be followed immediately by a Redis write in the same request scope.
8. **Always use Publisher Confirms in RabbitMQ producers.** Fire-and-forget is not acceptable for drone detection alerts.
9. **Always use Manual ACK in RabbitMQ consumers.** `BasicAck` only after successful SignalR broadcast. `BasicNack(requeue: true)` on failure.
10. **Proto files in `/proto/` are the single source of truth for gRPC contracts.** Never generate or modify `.cs` gRPC stubs by hand — always regenerate from proto via `Grpc.Tools`.
