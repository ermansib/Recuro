# Recuro Admin

The admin portal controls how every tenant's Recuro portal looks and behaves. It is one deployable
(a modular monolith): an ASP.NET Core API on **.NET 10 (LTS, C# 14)** that also serves the
**React + TypeScript** client.

It has two levels:

| Level | Who | What they control |
|---|---|---|
| Platform console | Recuro's own team | Tenants (create, plan, custom domain, suspend), each tenant's theme and default colour mode (Tenant themes), the theme library |
| Tenant admin | Each customer's HR admin | A view of the theme assigned to them, and every screen's on/off, header text, field labels, visibility, required flags and order |

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

Prerequisites: .NET 10 SDK, Node 24, and Docker (or a local PostgreSQL and Keycloak). In VS Code, the
C# Dev Kit opens `admin/server/Recuro.Admin.slnx`.

```bash
docker compose -f admin/docker-compose.yml up -d     # PostgreSQL on :5432, Keycloak on :8080

cd admin/server
dotnet run --project src/Recuro.Admin.Api --launch-profile http   # API on http://localhost:5080
                                                                    # applies migrations and seeds demo data

cd admin/client
npm install
npm run dev        # http://localhost:5174, proxies /api to the API
```

Or build the client once (`npm run build` in `admin/client`) and open http://localhost:5080: the API
serves it.

No Docker handy? Run the API on SQLite with the development persona sign-in instead of Keycloak:

```bash
dotnet run --project src/Recuro.Admin.Api --launch-profile http -- \
  --Database:Provider=Sqlite --ConnectionStrings:AdminDb="Data Source=recuro-admin.db" --Auth:Mode=Development
```

## Sign-in (Keycloak)

People sign in with [Keycloak](https://www.keycloak.org/) (free, Apache 2.0) using the authorization
code flow with PKCE. The client asks the API for `/api/v1/auth/config`, redirects to Keycloak, and
sends the access token as a bearer token. The API validates it (`Auth:Authority`, `Auth:Audience`)
and reads two claims:

| Claim | Value | Set in Keycloak as |
|---|---|---|
| `roles` | `platform-admin` or `tenant-admin` | realm roles |
| `tenant_id` | the tenant's id (tenant admins only) | user attribute, editable by Keycloak admins only |

`keycloak/recuro-realm.json` creates the `recuro` realm, the `recuro-admin` client and three demo
users for local runs (password `Recuro@2026`): `platform@recuro.example`, `admin@aurora.example` and
`admin@talentbridge.example`. Keycloak's own console is at http://localhost:8080 (admin / admin).
These demo accounts and passwords are for local development only; production realms are created
without them.

Tenant admins of a suspended tenant are refused. To add a tenant admin, create the user in Keycloak,
give them the `tenant-admin` role, and set `tenant_id` to the tenant's id from the platform console.

## Workspace sign-up

The main portal's "Create workspace" page calls `POST /api/v1/workspaces` (anonymous, rate limited per IP,
`SignUp:*` settings). The API validates the form, then:

1. creates the owner's account in Keycloak (admin REST API, as the `recuro-admin-provisioner` service
   account with only `manage-users` and `view-realm`): persona role (`hrta`/`hrhead`/`mdceo`) plus
   `tenant-admin`, and the `tenant_id` attribute. Keycloak keeps the password;
2. saves the workspace in the **`tenants`** table of `recuro_admin` (Starter plan, org-type defaults for SSO,
   MFA roles, careers tagline, locale, currency). If saving fails, the Keycloak account is removed again.

`GET /api/v1/workspaces/{slug}` returns an active workspace's sign-in settings. Configure Keycloak with
`AccountDirectory:BaseUrl` and `AccountDirectory:ClientSecret` (user secrets or environment variables outside
local dev); `AccountDirectory:Mode=Disabled` skips account creation for runs without Keycloak. An existing
Keycloak needs the new client added by a partial import of `keycloak/recuro-realm.json` (the realm import
only runs on first start).

`Auth:Mode=Development` swaps Keycloak for a persona picker (sent as an `X-Recuro-Persona` header).
The API refuses to start in that mode outside the Development and Testing environments.

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
