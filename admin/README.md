# Recuro Admin

The admin portal controls how every tenant's Recuro portal looks and behaves. It is one deployable
(a modular monolith): an ASP.NET Core API on **.NET 10 (LTS, C# 14)** that also serves the
**React + TypeScript** client.

It has two levels:

| Level | Who | What they control |
|---|---|---|
| Platform console | Recuro's own team | Tenants (create, plan, custom domain, suspend), the theme library |
| Tenant admin | Each customer's HR admin | Their theme and colour mode, and every screen's on/off, header text, field labels, visibility, required flags and order |

The main portal reads the result from `GET /api/v1/runtime/{tenantSlug}`: theme tokens (keyed by the
CSS variables in `frontend/src/styles/tokens.css`) plus the resolved screen layout.

## Layout

```
admin/
  server/                      .NET 10 solution (Recuro.Admin.slnx)
    src/Recuro.Admin.Domain          entities and rules: Tenant, ThemePreset (WCAG check), ScreenDefinition,
                                     TenantScreenConfiguration (locked fields, required ⇒ visible)
    src/Recuro.Admin.Application     use cases (plain handlers, no MediatR) and DTOs
    src/Recuro.Admin.Infrastructure  EF Core on PostgreSQL, migrations, tenant query filter, seed data
    src/Recuro.Admin.Api             minimal API endpoints, auth, serves the client from wwwroot
    tests/                           unit, integration (real API on SQLite) and architecture tests
  client/                      React + Vite admin UI (builds into the API's wwwroot)
  docker-compose.yml           PostgreSQL for local runs
```

## Run it

Prerequisites: .NET 10 SDK, Node 24, and PostgreSQL (or Docker). In VS Code, the C# Dev Kit opens
`admin/server/Recuro.Admin.slnx`.

```bash
docker compose -f admin/docker-compose.yml up -d     # PostgreSQL on localhost:5432

cd admin/server
dotnet run --project src/Recuro.Admin.Api --launch-profile http   # API on http://localhost:5080
                                                                    # applies migrations and seeds demo data

cd admin/client
npm install
npm run dev        # http://localhost:5174, proxies /api to the API
```

Or build the client once (`npm run build` in `admin/client`) and open http://localhost:5080: the API
serves it.

No PostgreSQL handy? Run the API on SQLite instead:

```bash
dotnet run --project src/Recuro.Admin.Api --launch-profile http -- \
  --Database:Provider=Sqlite --ConnectionStrings:AdminDb="Data Source=recuro-admin.db"
```

## Sign-in

In Development, the sign-in page lists personas (platform admin, or tenant admin of a demo tenant);
the client sends the choice in an `X-Recuro-Persona` header. This mode refuses to start outside the
Development and Testing environments.

Everywhere else `Auth:Mode` is `Oidc`: the API validates bearer tokens from any OpenID Connect
provider (`Auth:Authority`, `Auth:Audience`). Tokens carry a `roles` claim (`platform-admin` or
`tenant-admin`) and, for tenant admins, a `tenant_id` claim.

## Checks

```bash
cd admin/server && dotnet build && dotnet format --verify-no-changes && dotnet test
cd admin/client && npm run lint && npm run typecheck && npm test && npm run build
```

## Migrations

```bash
cd admin/server
dotnet tool restore
dotnet ef migrations add <Name> -p src/Recuro.Admin.Infrastructure -s src/Recuro.Admin.Api -o Persistence/Migrations
```
