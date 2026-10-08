# Recuro services: service map and build plan

Source: `Documentation/BackendUserStories.html` (RCU-BKD-001 v1.0, 18 services, ~95 stories; copy in
the project files as `recuro/backenduserstories.html`). Decisions behind this map:
[ADR 0001](adr/0001-microservices-foundation.md).

## Ground rules (every thread, every service)

1. **Database per service.** Each service owns `recuro_<service>` on PostgreSQL with its own login. No
   cross-service joins, no shared tables. Other services' data arrives by API call or by event.
2. **Talk through contracts only.** A service never references another service's projects. It may use
   only `BuildingBlocks`. The architecture tests fail the build otherwise.
3. **Sync for queries and user commands, async for propagation.** REST through the gateway for reads and
   user-initiated commands; every state change other services care about is an integration event sent
   through the transactional outbox. Never chain more than 2 synchronous hops.
4. **Events are CloudEvents 1.0** on the RabbitMQ topic exchange `recuro.events`, routing key = event
   type. Names come only from the catalog (`EventTypes`, RCU-BKD-001 §5). Each consumer declares its own
   payload record; JSON schemas go in `services/contracts/events/<type>.schema.json`.
5. **Multi-tenant from day one.** Every tenant-owned row implements `ITenantOwned`; `RecuroDbContext`
   filters and stamps `TenantId`. Tests prove tenant A cannot see tenant B.
6. **API responses match the frontend mocks** (`frontend/src/domain/types.ts`) field for field.
7. **Tenant configuration is split in two** (decided by the product owner on 2026-10-07):
   - **Presentation config lives in the admin portal** (`/admin`): branding, themes, screens, fields,
     labels and email-template overrides. Services read it from the admin API; they don't copy it.
   - **Business rules live in the Config service** (`services/Config`): DOA, TAT, offer, escalation and
     BGV matrices plus business calendars, versioned and pinned per workflow. Services resolve rules
     through the Config API and react to `config.version.activated`; none of them keeps its own copy
     of a matrix or hard-codes a rule. This may be revisited later; if it moves, only Config changes.
8. **Free / open-source only.** CI runs a licence check.

## Service map

| # | Service (folder) | Stories | Owns (data) | Publishes | Consumes | Calls (sync) | Port |
|---|---|---|---|---|---|---|---|
| — | **BuildingBlocks** | PLT-001..004 | outbox, inbox tables (in each service DB) | — | — | — | — |
| 0 | **Gateway** | GTW-001..005 | rate-limit state (Valkey) | — | — | every service (proxy); BFF: `/bff/candidates`, `/bff/dashboard/ta`; proxies Notification's SSE stream | 5100 |
| 1 | **Identity** | AUT-001..005 | user mirror (JIT from Keycloak), role/policy map, masking map | `identity.user.provisioned`, `identity.role.changed` | — | Keycloak admin API | 5101 |
| 2 | **Config** (rules) | CFG-001..003, 005 | versioned DOA / TAT / offer / escalation / BGV matrices, business calendars | `config.version.activated` | — | — | 5102 |
| 3 | **Audit** ✅ built here | AUD-001..003 | hash-chained audit entries, seals | — | **all** events | — | 5103 |
| 4 | **Notification** | NTF-001..006 | feed items, delivery log, email templates; SSE stream; preferences (NTF-005, P2) and scheduled dispatch (NTF-006, P1) not built yet | `notification.created`, `notification.email.dispatched/failed` | event matrix §5.6, including the reminder and chase events | Config (templates via admin), Identity (recipients) | 5104 |
| 5 | **Requisition** (MRF + JD) | REQ-001..008, JD screens | requisitions, tracker, job descriptions | `recruitment.mrf.*`, `recruitment.sourcing.unlocked` | `workflow.task.completed` | Config (resolve DOA), Workflow (create instance) | 5105 |
| 6 | **Workflow** | WFL-001..007 | workflow instances, tasks, SLA timers, delegations | `workflow.task.*`, `workflow.escalated`, `workflow.sla.*` | submit events | Config (route, calendar), Identity (PDP) | 5106 |
| 7 | **Candidate** | CND-001..006 | candidates (PII encrypted), consents, merges | `candidate.created/merged/purged` | `pipeline.application.final_rejected` | Identity (masking), Vendor (active check) | 5107 |
| 8 | **Pipeline** | PPL-001..008 | applications, stage history, TAT clocks, holds | `pipeline.*` | `recruitment.sourcing.unlocked`, `recruitment.mrf.cancelled`, `interview.selection.ratified`, `bgv.*`, `offer.accepted` | Requisition (sourcing gate), Candidate | 5108 |
| 9 | **Interview** | INT-001..007 | rounds, schedules, Annexure B assessments, selection summaries | `interview.*` | `pipeline.stage.changed` | Config (round templates), Workflow (ratification) | 5109 |
| 10 | **Bgv** | BGV-001..009 | BGV cases, checks, vendor refs, webhook dedupe | `bgv.*` | `pipeline.stage.changed`, `vendor.de_empanelled` | Config (check matrix), Vendor, Candidate (consent) | 5110 |
| 11 | **Offer** | OFR-001..007 | offers, CTC breakup, letters, verbal log | `offer.*` | `workflow.task.completed`, `bgv.cleared`, `bgv.adverse.flagged`, `bgv.resolved`, `candidate.purged` | Config (offer matrix), Workflow, Bgv (release gate) | 5111 |
| 12 | **Onboarding** | ONB-001..006 | onboarding cases, Day-1 checklist, documents, probation | `onboarding.*` | `offer.accepted`, `bgv.cleared/adverse.flagged` | Config (checklist) | 5112 |
| 13 | **Vendor** (P1) | VND-001..005 | vendors, empanelment gates, agreements, SLA snapshots | `vendor.*` | `bgv.check.updated` | — | 5113 |
| 14 | **Reporting** (P1) | RPT-001..005 | read-model projections, KPI snapshots | — | nearly all events | — | 5114 |
| 15 | **Careers** (public API) | CAR-001..007 | postings, public applications, throttle state | `career.job.applied` | `recruitment.sourcing.unlocked`, `recruitment.mrf.cancelled`, `pipeline.application.final_rejected` | Candidate, Pipeline (intake saga), Requisition (gate) | 5115 |
| 16 | **Employee** (portal API, P1) | EMP-001..005 | IJP applications, referrals | `employee.ijp.applied`, `employee.referral.submitted` | `recruitment.sourcing.unlocked`, `pipeline.stage.changed` | Candidate, Pipeline (intake saga) | 5116 |

Notes:

- Keycloak does sign-in, SSO, MFA and lockout (AUT-001/002). The Identity service adds what Keycloak
  doesn't: the PDP (`POST /decide`), the masking map, the user mirror and service-account tokens.
- Branding and email-template overrides (CFG-004) stay in the admin portal. The Config service owns the
  rules matrices, which change under dual approval and are pinned per workflow (CFG-003).
- The JD builder (S-05) has no service in RCU-BKD-001; it lives in Requisition because a JD belongs to
  one requisition and gates posting (CAR-007).

## Synchronous contracts

The owning service implements these shapes exactly, and callers code against them. Any change goes
through this file first. JSON is camelCase. Calls between services are authenticated as described in
"Service-to-service authentication" below.

### Service-to-service authentication (owner: Identity, RCU-AUT-005; used by every service)

This is the contract for the Identity PR that is still in review. Until it merges, calls with no user
have no token.

- **Calls made for a user** forward the caller's `Authorization` header first, so masking and RBAC
  apply to that user. `CorrelationHeadersHandler` forwards only the correlation ids.
- **Calls with no user** come from bus consumers, jobs and schedulers. On these calls
  `ServiceTokenHandler` (`Recuro.BuildingBlocks.Web.Auth`) runs on the `HttpClient` after any
  forwarding handler. It signs only requests that carry no `Authorization` or `X-Dev-User` header.
  It sends:
  - a client-credentials bearer token for the Keycloak client `recuro-svc-<service>`, whose
    service-account user holds the realm role `service`;
  - `X-Recuro-Tenant`, taken from `ScopeContext.Current`, which the bus processor and the web
    middleware set.
- **Tokens** are cached until 60 seconds before they expire, and fetched again after a 401.
- **Configuration** lives in the `ServiceAuth` section:
  - `ClientId` defaults to `recuro-svc-{Service:Name}`.
  - `ClientSecret` comes from `ServiceAuth__ClientSecret`. Only in Development does it default to
    `{ClientId}-dev-secret`. It is never committed for other environments.
  - `TokenEndpoint` is derived from `Auth:MetadataAddress` or `Auth:Authority`.
  - `RenewBefore` sets how early a token is renewed.
- **The receiving side**: a validated token with role `service` and no `tenant_id` claim takes its
  tenant from `X-Recuro-Tenant`. User tokens ignore that header, and the gateway strips it from
  outside requests.
- **`Auth:Mode=Development`**: the handler sends `X-Dev-User: recuro-svc-<service>`,
  `X-Dev-Roles: service` and `X-Dev-Tenant` instead of a token.
- **Local Keycloak** has 16 clients named `recuro-svc-<service>`. Compose sets `KC_HOSTNAME` so
  tokens carry the localhost issuer that the services expect.
- **What service accounts may see** is set by the `service` masking map below. Rules for individual
  clients come later.

### Config: resolve rules (owner: Config, callers: Requisition, Workflow)

**`GET /api/v1/resolve/doa?grade=M3&budget=in|oob&at=<ISO-8601>[&versionId=<guid>]`**

- `at` picks the version in force at that moment.
- `versionId` pins a version instead, which is how a running workflow re-reads its rules (CFG-003).
- `budget=oob` returns the out-of-budget variant, which has the extra approval leg (REQ-003).
- An unknown grade returns 404, and a bad query returns 400 with field errors.
- The response extends the frontend `DoaRoute` (`frontend/src/domain/types.ts`) with the version and
  the legs:

```json
{
  "configVersionId": "guid",
  "grade": "M3",
  "budgetStatus": "in | oob",
  "initiating": "string",
  "recommending": "string",
  "approving": "string",
  "approverRole": "hrhead | mdceo | ...",
  "bandLabel": "string",
  "overallTat": { "minDays": 5, "maxDays": 7, "label": "string" },
  "legs": [
    {
      "name": "string",
      "assignees": [{ "role": "string", "label": "string" }],
      "slaWorkingDays": 2,
      "escalation": [{ "role": "string", "label": "string", "afterWorkingDays": 1 }]
    }
  ]
}
```

**`GET /api/v1/resolve/working-days?from=<date>&days=<n>[&location=<code>][&versionId=<guid>]`**

This adds `n` working days to `from` using the business calendar for `location` (the tenant default
when it is omitted). It returns `{ "date": "YYYY-MM-DD", "configVersionId": "guid" }`. This is the
only working-day calculation in the backend; services never count holidays themselves.

Pipeline (PR #12) reads working days, holidays and the TAT matrix from Config. If Config can't be
reached, it counts weekends only and uses the FRD deadlines, so a stage clock never stops.

### Identity: masking, PDP and users (owner: Identity; callers: every service and the Gateway BFF)

All Identity endpoints accept a tenant member's token or a service token.

**`GET /api/v1/identity/masking/{role}/{resource}`** (RCU-AUT-004), for example
`/api/v1/identity/masking/mdceo/candidate`

- Returns `{ "resource": "candidate", "role": "mdceo", "version": "string", "fields": { "currentCtc": "hide", "email": "partial", ... } }`.
- Resources today are `candidate` and `approval`.
- The strategies are:
  - `hide`: the field isn't returned.
  - `partial`: only the last 4 characters are kept.
  - `hash`: a stable one-way hash.
- A field that isn't listed passes through unchanged.
- An unknown role or resource returns 404 `masking_map_not_found`. Callers treat that 404 as "hide
  every sensitive field", so a missing map fails closed.
- Callers cache the map per tenant, role and version for up to 5 minutes, and apply it when they
  serialize a response.
- The current map version is `masking-2026.10.1`.
- The role `service`, used by client-credentials tokens, has its own map, and
  `GET /api/v1/identity/masking/service/{resource}` returns 200:
  - On `candidate`, a service account sees `name` and `email`. `phone`, `summary`, `currentCtc` and
    `expectedCtc` are hidden.
  - On `approval`, `sensitive` is hidden.
  - Per-client rules, such as Offer needing CTC, come with the Keycloak service-account clients
    (RCU-AUT-005).
- Candidate (PR #12) reads its masking rules from this endpoint. For an unknown role it hides every
  personal field. If Identity can't be reached, it uses the last rules it cached, and if it has none,
  the FRD §3.2 table.

**`POST /api/v1/identity/decide`** (the PDP, RCU-AUT-003)

- Request: `{ "actor": { "id", "roles": [] }, "action": "string", "resource": { "type", "id", "assigneeIds": [] }, "context": { "mfa": bool } }`
- Response: `{ "allow": bool, "reasons": [], "policyVersion": "string", "ttlSeconds": 60 }`
- An unknown action is denied.
- Action keys are the frontend capability keys plus these:
  - `offer.approve`
  - `bgv.decideAdverse`, which needs an MFA step-up for `hrhead` and `mdceo`
  - `vendor.empanel`, `reports.view`, `ijp.apply`, `referral.submit`
  - `workflow.task.decide`, which is allowed for the assignee only
- Callers may cache a decision for `ttlSeconds`.

**`GET /api/v1/identity/me`** provisions the user just in time from the token and returns the frontend
`User`. **`GET /api/v1/identity/users?role=<role>`** lists the tenant's users by role, which is how
Workflow and Notification find assignees and recipients.

### Config: matrices and versions (owner: Config)

The `resolve/doa` and `resolve/working-days` endpoints are defined above. Config also exposes:

- **`GET /api/v1/resolve/matrices/{doa|tat|offer|escalation|bgv|calendar}?at=<ISO>[&versionId=<guid>]`**
  returns `{ configVersionId, matrixType, number, effectiveFrom, content }`.
- **`GET /api/v1/config/rules`** returns the frontend `RuleConfig`.
- **`/api/v1/config/{type}/versions`** is version admin: propose, revise, approve and reject, with dual
  approval. Activation publishes `config.version.activated`.

Keys that other services use:

| Kind | Keys |
|---|---|
| Escalation issues | `tat-breach-sourcing`, `adverse-bgv`, `ctc-deviation`, `feedback-delay`, `candidate-grievance`, `vendor-sla-breach` |
| TAT stages | `mrf-approval`, `sourcing`, `interview`, `bgv`, `offer-issuance`, `offer-to-joining`, `onboarding-day1`, `overall-junior`, `overall-managerial`, `overall-kmp` |
| Calendar location | `default` when none is given |

### Vendor: consultant status (owner: Vendor, wave 2; caller: Candidate)

**`GET /api/v1/vendors/{vendorId}/status`** (RCU-CND-005, VND-001)

This returns `{ "vendorId": "string", "status": "active | pending | off", "active": true }`. An unknown
id returns 404, which the caller treats as not active (400 `vendor_not_active`). Until Vendor ships,
Candidate's `UncheckedVendorDirectory` accepts every id and logs a warning. Vendor also publishes
`vendor.de_empanelled`, so a caller may cache a status for up to 5 minutes.

### Gateway BFF: log a candidate (owner: Gateway; calls Candidate, then Pipeline)

**`POST /bff/candidates`** with the frontend `LogCandidateInput` body
(`frontend/src/api/contract.ts`). It returns 201 with a `PipelineCard`, the same shape `logCandidate`
returns in the mock.

1. `POST /api/v1/candidates` with the candidate fields and consent. Errors come back as is,
   including 409 `duplicate_candidate`. That matches the mock (RCU-PIP-002), where HR-TA decides
   whether the person is a duplicate.
2. `POST /api/v1/pipeline/applications` with `{ reqId, candidateId }`. A 409 means sourcing is
   locked or the candidate already applied, and it comes back as is.

The client's `Idempotency-Key` is forwarded to both calls with a step suffix (`<key>:candidate`,
`<key>:application`). If the first call succeeded and the second failed, a retry replays the stored
candidate response instead of tripping the duplicate check, then creates the application. No compensation is needed: a candidate without an application is valid data, and it ages
out under the retention policy. The BFF forwards the caller's bearer token and correlation ids.

### Gateway BFF: TA dashboard (owner: Gateway; each area owns its fragment)

**`GET /bff/dashboard/ta`** returns the frontend `DashboardData` (`frontend/src/domain/types.ts`). The
gateway calls these four services in parallel, forwarding the caller's token, and merges what comes
back:

| Order | Service | Fragment endpoint | Tiles it owns |
|---|---|---|---|
| 1 | Requisition (5105) | `GET /api/v1/requisitions/dashboard/ta` | Open MRFs |
| 2 | Offer (5111) | `GET /api/v1/offers/dashboard/ta` | Offers Pending |
| 3 | Bgv (5110) | `GET /api/v1/bgv/dashboard/ta` | BGV in Progress |
| 4 | Pipeline (5108) | `GET /api/v1/pipeline/dashboard/ta` | Joining ≤ 30d, TAT Breaches |

- Each fragment returns any subset of `{ stats[], tatBreaches[], pipeline[], kpis[], upcoming[] }`,
  using exactly the item shapes in `DashboardData`.
- The gateway merges the fragments in the order shown in the table and sets `dateLabel` and
  `kpiPeriodLabel` itself. Sources, order, tiles and the timeout are configuration (`Bff:Dashboard`).
- A fragment that fails, or takes longer than 2 seconds (`SourceTimeout`), contributes its tiles with
  the value "—", so the dashboard still loads. The `X-Recuro-Degraded` response header names the
  sources that didn't answer, so the client can tell "—" from zero.
- Each service owns its own fragment and builds it from its own data only.

### Live updates: notification stream (owner: Notification; proxied by Gateway, GTW-003)

- Notification serves Server-Sent Events at `GET /stream/notifications`, scoped to the caller's
  tenant and user. The gateway proxies it under the same path, with buffering and its request timeout
  turned off for that route.
- Each event's `data` is the bell item that `GET /notifications` returns.
- The browser `EventSource` API can't send a bearer token, so the frontend uses a fetch-based SSE
  client that sends `Authorization` and reconnects with `Last-Event-ID`.

### Reminder and chase events (catalog additions)

These three types are now in `EventTypes`, with schemas in `services/contracts/events`. The wave-2
producers publish them. Notification needs a matrix row for each, which isn't built yet, to remind
the people named by id. All three are idempotent per subject and
threshold, so a retry never sends a second reminder.

| Type | Producer | When | Payload |
|---|---|---|---|
| `workflow.task.reminder_due.v1` | Workflow | An open task reaches 50% and then 100% of its SLA (WFL-003). Escalation stays `workflow.escalated.v1`. | `taskId, instanceId, leg, subjectType, subjectId, assigneeIds[], assigneeRole, thresholdPercent, dueAt` |
| `interview.feedback.reminder_due.v1` | Interview | Feedback is still missing 24h after the interview (INT-004). `interview.feedback.overdue.v1` follows at 48h. | `interviewId, appId, reqId, round, pendingInterviewerIds[], endedAt, overdueAt` |
| `offer.chase_due.v1` | Offer | A sent offer is unanswered after 3 working days, then weekly until it is accepted, declined, withdrawn or expires (OFR-006). | `offerId, appId, reqId, candidateId, sentAt, chaseNumber, expiresAt` |

**Workflow assigns tasks to roles, not people.** In every `workflow.*` event, `assignee`,
`assigneeRole` and `escalateTo` are role keys (`hrta`, `hrhead`, `mdceo`, ...), never user ids.
Notification sends each one to that role's users in the tenant. `assigneeIds` on
`workflow.task.reminder_due.v1` stays in the payload for person-level tasks later, but it is empty
today. Adding `assigneeRole` was additive, so the event stays v1.

Escalating `interview.feedback.overdue.v1` to the HOD needs a recipient lookup that Identity is adding
to `GET /api/v1/identity/users`. Until then, Notification sends it to the panel and HR-TA.

### Fields consumers already rely on, for events wave 2 will publish

Offer adds `offer.accepted.v1.schema.json` when it first publishes the event, and that schema must
carry at least these fields:

| Event | Field | Type | Who reads it |
|---|---|---|---|
| `offer.accepted.v1` | `appId` | string, required | Pipeline moves the application from Offer to PreBoarding |
| `offer.accepted.v1` | `joiningDate` | `YYYY-MM-DD`, optional | Pipeline's "Joining ≤ 30d" dashboard tile (PR #17) |

Pipeline's fragment (`GET /api/v1/pipeline/dashboard/ta`) is live. If `joiningDate` is missing, the
application still moves to PreBoarding, but it isn't counted in that tile.

### Integration event change: `pipeline.application.final_rejected` v1

The payload is `{ appId, reqId, candidateId, reason, regretSendAt, retainUntil }`, with both dates as
`YYYY-MM-DD`. `candidateId` and `retainUntil` are additive, so v1 stays v1 and tolerant readers that
ignore them keep working. The schema is in
`services/contracts/events/pipeline.application.final_rejected.v1.schema.json`. Candidate uses
`retainUntil` for its purge date, and Notification uses `regretSendAt` for the regret email.

Contract changes follow these rules:

- Adding an optional field, or a field that every producer always sets, keeps the version.
- Removing or renaming a field, or changing its meaning, needs `.v2` and a period in which both
  versions are published.

## Sagas (owner runs the orchestration, compensations required)

| Saga | Owner | Steps |
|---|---|---|
| MRF approval | Requisition | submit → Config resolve (pin version) → Workflow instance → approved → `sourcing.unlocked`; compensation: revert to Draft |
| Candidate intake | Careers / Employee | consents → Candidate create (dupe-aware) → Requisition gate → Pipeline application; compensation: candidate tombstone |
| Offer → joining | Offer → Onboarding | band/route → Workflow → Bgv release gate → letter + e-sign → accepted → Onboarding chain |
| Adverse BGV | Bgv | flagged → Pipeline hold + Offer lock → Workflow escalation → resolved (clear or rescind) |

## Build order

The foundation PR (this one) ships BuildingBlocks, the gateway skeleton, docker-compose, CI, the
architecture tests and the Audit service as the worked example. After it merges, one thread per lane:

| Wave | Thread (lane) | Services | Why now |
|---|---|---|---|
| 1 | Identity & rules | Identity, Config | Every other service calls the PDP, masking map and DOA resolver |
| 1 | Notifications & gateway | Notification, Gateway BFF + SSE | The bell and dashboard tiles; NTF consumes events from day one |
| 1 | Demand & approvals | Requisition (+JD), Workflow | The MRF approval saga, the core loop's start |
| 1 | Candidates & pipeline | Candidate, Pipeline | Candidate truth, stage machine, kanban board |
| 2 | Assessment & offer | Interview, Offer | Need Pipeline stages and Workflow |
| 2 | Verification & vendors | Bgv, Vendor | Need Pipeline and the release gate contract |
| 2 | Public intake | Careers, Employee | Need Candidate + Pipeline for the intake saga |
| 3 | Lifecycle & reporting | Onboarding, Reporting | Consume offer and BGV events; reporting reads everything |

Wave-1 threads can run in parallel because they meet only through the event catalog and HTTP
contracts. A wave-2 thread may start before wave 1 merges by coding against the contracts and stubbing
the calls.

## Per-service checklist (Definition of Done, RCU-BKD-001 §9)

- `services/<Name>/src/Recuro.<Name>.{Domain,Application,Infrastructure,Api}` + `tests/`; add to
  `Recuro.Services.slnx`, add a `Dockerfile`, a compose entry under the `apps` profile and a gateway route.
- `appsettings.Development.json` connection string to `recuro_<name>` (the database already exists in
  compose); port from the table above.
- Domain rules and state machines as data (`TransitionTable<T>`), unit-tested ≥ 80 %.
- Every endpoint: authorization policy, validator, OpenAPI summary, frontend DTO shape.
- Every state change: outbox event from the catalog + audit (Audit mirrors bus events automatically).
- Integration tests on Testcontainers, including tenant isolation and a negative RBAC test.
- Saga steps have a compensation and a test exercising it.
