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
| 0 | **Gateway** | GTW-001..005 | rate-limit state (Valkey) | — | `notification.created` (SSE, later) | every service (proxy), BFF fan-out | 5100 |
| 1 | **Identity** | AUT-001..005 | user mirror (JIT from Keycloak), role/policy map, masking map | `identity.user.provisioned`, `identity.role.changed` | — | Keycloak admin API | 5101 |
| 2 | **Config** (rules) | CFG-001..003, 005 | versioned DOA / TAT / offer / escalation / BGV matrices, business calendars | `config.version.activated` | — | — | 5102 |
| 3 | **Audit** ✅ built here | AUD-001..003 | hash-chained audit entries, seals | — | **all** events | — | 5103 |
| 4 | **Notification** | NTF-001..006 | feed items, delivery log, preferences, email templates | `notification.created`, `notification.email.dispatched/failed` | event matrix §5.6 | Config (templates via admin), Identity (recipients) | 5104 |
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
through this file first. JSON is camelCase. Until service accounts exist, callers forward the incoming
`Authorization` bearer token on the outgoing call. `CorrelationHeadersHandler` forwards only the
correlation ids, so the token is the caller's job.

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
