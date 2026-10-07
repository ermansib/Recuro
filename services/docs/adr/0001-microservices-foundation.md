# ADR 0001: Microservices foundation

- Status: Accepted
- Date: 2026-10-07
- Stories: RCU-PLT-001..004, RCU-GTW-001/004, RCU-AUD-001/002 (RCU-BKD-001 v1.0)

## Context

RCU-BKD-001 splits the backend into 18 services, built in parallel by several threads. The services
must stay independent (database per service, contract-first, outbox, idempotency, correlation) while
sharing one way of doing the plumbing. Project rules: .NET 10 LTS / C# 14, free and open-source only,
PostgreSQL, Redis-compatible cache, Keycloak, multi-tenant with a TenantId on every row, and API shapes
that match the React app's mock DTOs.

## Decision

| Concern | Decision | Instead of |
|---|---|---|
| Runtime | .NET 10 LTS, C# 14, Clean Architecture per service (Domain, Application, Infrastructure, Api) | RCU-BKD-001 §1.3 defaults (NestJS / Spring) |
| Shared code | `services/BuildingBlocks` only: Domain kernel, Application (CQRS, decorators, event contracts), Infrastructure (EF Core base context, outbox, inbox, RabbitMQ, locks), Web (hosting defaults, auth, middleware). No other code is shared between services. | Shared "contracts" projects per service |
| CQRS | Own `ICommandHandler` / `IQueryHandler`, Scrutor decorators for validation (FluentValidation) and logging | MediatR 13+ (commercial) |
| Database | PostgreSQL 17, one database and login per service (`recuro_<service>`), EF Core + Npgsql, snake_case names | Shared database, SQL Server |
| Tenancy | Shared schema with `TenantId` (BQ-08). `RecuroDbContext` adds a query filter and index to every `ITenantOwned` entity, stamps new rows, refuses cross-tenant writes; no tenant in scope ⇒ queries return nothing | Schema-per-tenant (kept possible later via a per-tenant connection string) |
| Messaging | RabbitMQ 4, one topic exchange `recuro.events`, routing key = event type, one durable queue + DLQ per service. Own small transport on `RabbitMQ.Client` 7 (publisher confirms, in-process retries with backoff, then dead-letter) | Kafka (heavier to run), MassTransit 9+ (commercial), Wolverine (brings its own mediator next to ours) |
| Outbox / inbox | Outbox table in each service DB, written in the same `SaveChanges` as the change (via domain-event handlers). A relay claims rows with `FOR UPDATE SKIP LOCKED` (safe with replicas). Consumers dedupe per handler on the event id in an inbox table, in the handler's transaction | Dual writes |
| Event envelope | CloudEvents 1.0 structured JSON + extensions `tenantid`, `correlationid`, `traceparent`, `actorid`, `actorname`, `actorrole` | Custom envelope |
| Event contracts | Event names in one catalog (`EventTypes`); each consumer declares its own payload record (tolerant reader); JSON schemas in `services/contracts/events` | Shared DTO assemblies (would couple deployments) |
| Gateway | YARP. Validates the Keycloak JWT (401 before any service), route rate limits (429 + Retry-After), X-Request-ID / X-Correlation-ID, strips identity headers | Ocelot, a paid API manager |
| Service auth | Every service validates the bearer token itself (zero trust); the gateway forwards it. Service-to-service calls use Keycloak client-credentials tokens with the `service` role (Identity thread) | Trusting `X-User-*` headers from the gateway, which needs strict network isolation that cheap hosts can't guarantee |
| Validation errors | 400 with `{ code, errors: [{ field, code, message }] }`, matching the frontend mock and CLAUDE.md | 422 from RCU-BKD-001 (the frontend contract wins) |
| Idempotency | `Idempotency-Key` on mutating requests; replays from the distributed cache for 24 h, scoped to tenant + user + route | — |
| Cache | Valkey 8 (BSD, Redis protocol) via `IDistributedCache` | Redis 8 (AGPL / SSPL licences) |
| Observability | Serilog JSON to stdout with RequestId / CorrelationId / TenantId / trace ids; OpenTelemetry traces (OTLP when configured) and Prometheus `/metrics`; `/health` and `/ready` | Application Insights |
| Schedulers | `PostgresLocks` advisory locks so a job runs on one replica at a time | Quartz clustering, Temporal (later if sagas need it) |
| Boundaries | `services/tests/Recuro.ArchitectureTests` (NetArchTest + project-file checks) fails CI if a layer points outward, a service references another service, shared code references a service, two services share a database or a DbContext skips the tenant-aware base | Code review alone |
| Licences | `nuget-license` in CI against `licenses/allowed-licenses.json` | — |

## Consequences

- New services copy Audit's shape and get tenancy, outbox, auth, logging, health and metrics from two
  calls (`AddRecuroServiceDefaults`, `UseRecuroServiceDefaults`) plus `AddRecuroPersistence` and
  `AddRecuroMessaging`.
- Changing BuildingBlocks affects every service; keep it small and change it in its own PR.
- We own a small messaging layer (~400 lines) instead of a framework. If it grows (sagas with timeouts,
  scheduled messages), revisit Wolverine or Temporal in a new ADR.
- Services can still be deployed together as a modular monolith (RCU-BKD-001 §1.2 escape hatch) without
  changing boundaries, because the boundaries are enforced by tests, not by deployment.
