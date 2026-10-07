# Recuro services

The backend: .NET 10 (LTS, C# 14) microservices behind a YARP gateway, one PostgreSQL database per
service, RabbitMQ for integration events, Valkey (Redis) for cache and Keycloak for sign-in. Free and
open-source only.

- Service map, ownership and build order: [docs/architecture.md](docs/architecture.md)
- Why it's built this way: [docs/adr/0001-microservices-foundation.md](docs/adr/0001-microservices-foundation.md)
- Coding rules: the `recuro-dotnet-developer` skill and the root `CLAUDE.md`

## Layout

```
services/
  BuildingBlocks/        shared kernel, the only code services share
    Recuro.BuildingBlocks.Domain           Entity, AggregateRoot, ITenantOwned, Result, Error, TransitionTable
    Recuro.BuildingBlocks.Application      ICommand/IQuery + handlers, validation/logging decorators,
                                           ITenantContext/ICurrentUser, integration events + EventTypes catalog
    Recuro.BuildingBlocks.Infrastructure   RecuroDbContext (tenant filter + stamping), outbox, inbox,
                                           RabbitMQ relay/consumer, advisory locks
    Recuro.BuildingBlocks.Web              AddRecuroServiceDefaults: Serilog, OpenTelemetry, health,
                                           Keycloak JWT, correlation, idempotency, Result → HTTP
  Gateway/               YARP gateway (port 5100)
  Audit/                 the worked example: append-only, hash-chained audit trail (port 5103)
  contracts/events/      JSON schemas for integration events
  tests/                 architecture tests for every service
  infra/postgres/        creates one database and login per service
  licenses/              licence allow-list for the CI check
  docker-compose.yml     PostgreSQL, Valkey, RabbitMQ, Keycloak (+ apps, + Jaeger)
```

## Run it

Prerequisites: .NET 10 SDK, Docker. In VS Code, the C# Dev Kit opens `services/Recuro.Services.slnx`.

```bash
cd services
docker compose up -d                 # PostgreSQL :55433, Valkey :6379, RabbitMQ :5672 (UI :15672), Keycloak :8080
                                     # Keycloak already running from admin/? use: docker compose up -d postgres redis rabbitmq
dotnet run --project Audit/src/Recuro.Audit.Api --launch-profile http     # http://localhost:5103
dotnet run --project Gateway/src/Recuro.Gateway --launch-profile http     # http://localhost:5100
```

Or build and run everything in containers: `docker compose --profile apps up -d --build`.

Ports clash on your machine? Override them: `RECURO_PG_PORT`, `RECURO_REDIS_PORT`, `RECURO_AMQP_PORT`,
`RECURO_RABBIT_UI_PORT`, `KEYCLOAK_PORT` (then pass matching `--ConnectionStrings:*` / `--Auth:Authority`).

### Sign in

The realm is shared with the admin portal (`admin/keycloak/recuro-realm.json`). The main portal's
client is `recuro-portal`; tokens carry `roles` (`hrta`, `hrhead`, `mdceo`, `employee`, `candidate`,
`service`), `tenant_id` and the audience `recuro-api`. Demo user: `hrta@aurora.example` /
`Recuro@2026` (local only).

Without Keycloak, run a service with `--Auth:Mode=Development` and send `X-Dev-User`, `X-Dev-Roles`
and `X-Dev-Tenant` headers. This mode refuses to start outside Development and Testing.

```bash
curl -H "X-Dev-User: u1" -H "X-Dev-Roles: hrhead" -H "X-Dev-Tenant: 6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a01" \
  http://localhost:5103/api/v1/audit
```

### Every service exposes

| Path | What |
|---|---|
| `/health` | liveness |
| `/ready` | readiness: its database and RabbitMQ |
| `/metrics` | Prometheus |
| `/openapi/v1.json` | OpenAPI document |

## Check before you push

```bash
dotnet build Recuro.Services.slnx                       # warnings are errors
dotnet format Recuro.Services.slnx --verify-no-changes
dotnet test Recuro.Services.slnx                        # needs Docker for Testcontainers
dotnet tool restore && dotnet nuget-license -i Recuro.Services.slnx -t -a licenses/allowed-licenses.json \
  -mapping licenses/license-url-mappings.json -override licenses/package-overrides.json
```

## Add a service

1. Copy the shape of `Audit/`: four projects under `src/`, tests under `tests/`, a `Dockerfile`.
2. `Program.cs`: `AddRecuroServiceDefaults("<name>")`, your `Add<Name>Application()` /
   `Add<Name>Infrastructure()` (which call `AddRecuroPersistence` and `AddRecuroMessaging`), then
   `UseRecuroServiceDefaults()` and your endpoints.
3. Your DbContext inherits `RecuroDbContext`; tenant-owned entities implement `ITenantOwned`.
4. Publish events with `IIntegrationEventPublisher` from a domain-event handler; subscribe with
   `.Subscribe<TPayload, THandler>(EventTypes.X.Y)`.
5. Connection string to `recuro_<name>` in `appsettings.Development.json`, port from the service map,
   add the projects to `Recuro.Services.slnx`, a compose entry and a gateway route.
6. Migrations: `dotnet ef migrations add InitialCreate --project <Name>/src/Recuro.<Name>.Infrastructure --output-dir Persistence/Migrations`.

The architecture tests pick up the new service automatically.
