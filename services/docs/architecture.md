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
| 9 | **Interview** | INT-001..007 | rounds, schedules, Annexure B assessments, selection summaries | `interview.*` | `pipeline.stage.changed`, `workflow.task.completed` | Config (interview matrix), Requisition (job description), Identity (PDP), Workflow (ratification) | 5109 |
| 10 | **Bgv** | BGV-001..009 | BGV cases, checks, consent, vendor refs, webhook dedupe | `bgv.*` | `pipeline.stage.changed`, `vendor.de_empanelled`, `workflow.task.completed` (type `bgv-adverse`) | Config (check matrix, escalation), Vendor (status), Workflow (adverse saga), Identity (masking) | 5110 |
| 11 | **Offer** | OFR-001..007 | offers, CTC breakup, letters, verbal log | `offer.*` | `pipeline.stage.changed`, `workflow.task.completed`, `bgv.case.initiated`, `bgv.cleared`, `bgv.adverse.flagged`, `bgv.resolved`, `candidate.purged` | Config (offer matrix, working days), Requisition, Identity (masking), Workflow; release gate fed by `bgv.*` events | 5111 |
| 12 | **Onboarding** | ONB-001..006 | onboarding cases, Day-1 checklist, documents, probation | `onboarding.*` | `offer.accepted`, `bgv.cleared/adverse.flagged` | Config (checklist) | 5112 |
| 13 | **Vendor** (P1) | VND-001..005 | vendors, empanelment gates, agreements, SLA snapshots | `vendor.*` | `bgv.check.updated` | — | 5113 |
| 14 | **Reporting** (P1) | RPT-001..005 | read-model projections, KPI snapshots | — | nearly all events | — | 5114 |
| 15 | **Careers** (public API) | CAR-001..007 | postings, public applications, throttle state | `career.job.applied` | `recruitment.sourcing.unlocked`, `recruitment.mrf.cancelled`, `pipeline.application.final_rejected`, `pipeline.stage.changed`, `notification.email.dispatched` (regret delivery, template `candidate.regret`) | Candidate, Pipeline (intake saga), Requisition (gate) | 5115 |
| 16 | **Employee** (portal API, P1) | EMP-001..005 | IJP applications, referrals | `employee.ijp.applied`, `employee.referral.submitted` | `recruitment.sourcing.unlocked`, `recruitment.mrf.cancelled`, `pipeline.stage.changed`, `pipeline.application.final_rejected` | Candidate, Pipeline (intake saga) | 5116 |

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

Built in PR #21 (`ServiceTokenHandler` in `Recuro.BuildingBlocks.Web.Auth`).

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
  tenant from `X-Recuro-Tenant`. User tokens ignore that header. The gateway strips
  `X-Recuro-Tenant`, along with `X-User-Id`, `X-User-Roles` and `X-Tenant-Id`, from every outside
  request, so only a service inside the network can set it.
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

### Vendor: status (owner: Vendor; callers: Candidate, Bgv; PR #24)

**`GET /api/v1/vendors/{vendorId}/status`** (RCU-CND-005, VND-001). Any signed-in caller, user or
service, may read it.

- Returns `{ "vendorId", "status": "active | pending | off", "active": bool, "name", "type": "Consultant | BgvAgency" }`.
  `name` and `type` were added later; the change is additive.
- An unknown id, or one that isn't a GUID, returns 404. The caller treats that as not active
  (Candidate returns 400 `vendor_not_active`).
- A caller may cache a status for up to 5 minutes, because Vendor also publishes
  `vendor.de_empanelled`.

The other Vendor routes under `/api/v1/vendors`, all for HR Head:

- `GET /` lists vendors.
- `POST /` registers a vendor.
- `GET /{vendorId}` returns one vendor.
- `PUT /{vendorId}/empanelment` updates the six empanelment gates.
- `POST /{vendorId}/empanel` and `POST /{vendorId}/de-empanel` change the vendor's status.

The fee band (5–8.33% of CTC) stays in Vendor's own configuration until Config has a vendor matrix.

### Background verification (owner: Bgv 5110; PR #24)

Routes under `/api/v1/bgv`. `{caseRef}` is either the `appId` or the case id.

| Route | Purpose |
|---|---|
| `GET /dashboard/ta` | dashboard fragment (see the TA dashboard section) |
| `POST /cases` | start a case. Consent is owned by Bgv, so a missing consent returns 400 `consent_required` |
| `GET /cases/{caseRef}` | case with its checks |
| `GET /cases/{caseRef}/release-gate` | whether an offer may be released (OFR-007): `{appId, caseId, cleared, status, blockers[]}`. The `service` role can read it |
| `POST /cases/{caseRef}/release` | release the gate. Returns 409 `bgv_release_blocked` while checks are open or adverse |
| `POST /cases/{caseRef}/checks/{checkType}` | update one check |
| `POST /cases/{caseRef}/adverse` | report an adverse finding, which starts the adverse saga |
| `GET /cases/reassignment`, `POST /cases/reassign` | list cases left with a de-empanelled vendor, and move them to an active vendor |

**Events** (schemas in `services/contracts/events`). The four case events below all carry `caseId, appId, reqId, vendorId`.

| Type | Notes |
|---|---|
| `bgv.case.initiated.v1` | |
| `bgv.check.updated.v1` | |
| `bgv.adverse.flagged.v1` | |
| `bgv.cleared.v1` | carries `onTime` for vendor SLA |
| `bgv.resolved.v1` | `status` `ResolvedCleared` or `ResolvedAdverse`, `decision` `override` or `rescind`, plus `checkType` and `decidedBy`. There is no `outcome` field |
| `vendor.empanelled.v1` | |
| `vendor.de_empanelled.v1` | carries `reassignmentHint: "reassign-open-cases"` |

**Adverse saga.** Bgv starts a Workflow instance with type `bgv-adverse`, subject `BgvCase/{caseId}`
and presentation kind `AdverseBgv`.

- Its legs are HR Head, then MD/CEO, taken from Config's `adverse-bgv` escalation.
- When `workflow.task.completed` arrives with action `rescind` or `override`, Bgv publishes
  `bgv.resolved.v1`.
- Pipeline and Offer react to `bgv.adverse.flagged` and `bgv.resolved`.

**No Bgv TAT-breach event.** Pipeline's `pipeline.tat.breached.v1` already covers the BGV stage,
using the same Config TAT, so escalation happens only once.

**Outgoing calls.** Bgv's HTTP clients run `ForwardCallerHandler` and then `ServiceTokenHandler`. A
call made for a user carries that user's token, and a call from a consumer or job gets a service
token.

**Planned (not built yet):**

- **Identity:** a `bgvCheck` masking resource with `sensitiveNote` hidden for `mdceo`, `employee`,
  `candidate` and `service`. Until it exists, Bgv applies the same rule locally.
- **Config** (optional): BGV matrix rows may carry
  `appliesWhen { always, grades[], anyFlags[] }`. Until they do, Bgv uses the built-in FRD §5.5
  rules.

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

### Careers and Employee portal APIs (owners: Careers 5115, Employee 5116; PR #22)

**Public careers site** (CAR-001..007). These routes are anonymous. The gateway route
`careers-public` applies the `public` rate limit, and the tenant comes from the URL, never from a
token.

| Method and path | Request | Response |
|---|---|---|
| `GET /api/v1/careers/public/{tenantId}/jobs` | `q`, `location`, `industry`, `cursor`, `limit` | `{ items: JobPosting[], nextCursor }`, using the frontend `JobPosting` shape |
| `POST /api/v1/careers/public/{tenantId}/applications` | frontend `PublicApplicationInput` | `{ appId, position }` |
| `GET /api/v1/careers/public/{tenantId}/applications/{appId}/status` | — | `{ appId, stage, status }` |

A public applicant is never told that they match an existing candidate. `duplicateOf` is not returned,
and the possible duplicate is flagged to HR through `career.job.applied.v1` instead.

**Careers HR routes**, for HR staff:

- `GET /api/v1/careers/postings` lists postings.
- `PUT /api/v1/careers/postings/{reqId}` creates or updates a posting.
- `POST /api/v1/careers/postings/{reqId}/publish` takes `{ openBeforeIjpWindowEnds, justification }`.
  Publishing externally before the IJP window ends is HR Head only and needs a justification.
- `POST /api/v1/careers/postings/{reqId}/unpublish` takes `{ reason }`.

**Employee portal** (EMP-001..005):

| Route | Who |
|---|---|
| `GET /api/v1/employee/ijp` | open IJP postings, for any employee |
| `PUT /api/v1/employee/ijp/{reqId}` | HR-TA |
| `POST /api/v1/employee/ijp/{reqId}/applications` | employee |
| `POST /api/v1/employee/referrals` | employee; `coiAccepted` must be true |
| `GET /api/v1/employee/me/applications`, `GET /api/v1/employee/me/referrals` | the signed-in employee |

**IJP window.** Careers and Employee each compute the window as the time of the
`recruitment.sourcing.unlocked` event plus 5 working days, using Config's `resolve/working-days`.
There is no shared state, and both get the same answer from the same calendar. The 5 days are a
service option until Config carries an IJP rule. When it does, both services read it from there.

**Events** (schemas in `services/contracts/events`):

| Type | Payload |
|---|---|
| `career.job.applied.v1` | `appId, jobId, reqId, candidateId, possibleDuplicate, consents { dataPrivacy, conflictOfInterest }` |
| `employee.ijp.applied.v1` | `employeeId, reqId, appId, candidateId` |
| `employee.referral.submitted.v1` | `referralId, referrerId, reqId, appId, candidateId, relationship, bonusEligible, possibleDuplicate` |

**Intake calls** to Candidate and Pipeline run as the `service` role with BuildingBlocks'
`ServiceTokenHandler` (RCU-AUT-005), using Keycloak clients `recuro-svc-careers` and
`recuro-svc-employee`. The Config calendar call forwards the HR user's own credentials.

**Candidate additions the intake sagas use** (built in PR #25):

- `POST /api/v1/candidates/{id}/tombstone` (role `service` only) is the compensation step of the
  candidate-intake saga when the application can't be created. It returns:
  - 204, also on a repeat call (idempotent).
  - 404 for an unknown id.
  - 409 `legal_hold` when the candidate is under legal hold.
  - 409 `candidate_in_use` when the candidate has an active application.

  On success it publishes `candidate.purged` with `purgeScope` `"anonymised"`.
- The 409 `duplicate_candidate` problem carries an `existingId` extension member (the existing
  candidate's id as a string guid), so the intake sagas can link to it. The title is unchanged, and
  the gateway's `/bff/candidates` still returns the 409 unchanged to HR-TA.
- BuildingBlocks' `Error` has an optional `Extensions` dictionary, which `ToProblem` merges into the
  `ProblemDetails`. It is additive, and any service can use it.

### Interview and Offer (owners: Interview 5109, Offer 5111; PR #29)

Databases `recuro_interview` and `recuro_offer`.

**Interview** (RCU-INT):

- Synchronous calls: Config `resolve/matrices/interview` (round templates), Requisition
  `GET /api/v1/requisitions/{reqId}/job-description`, Identity `POST /identity/decide` for
  `assessment.submit`, and Workflow for ratification.
- Consumes `pipeline.stage.changed.v1` and `workflow.task.completed.v1`.
- Publishes:

| Type | Notes |
|---|---|
| `interview.scheduled.v1` | adds `roundId`, `panel` and `dueAt` (feedback due) |
| `interview.feedback.submitted.v1` | one per assessment revision; `roundComplete` when the panel is done |
| `interview.feedback.reminder_due.v1` | see the reminder events below |
| `interview.feedback.overdue.v1` | adds `roundId`, `panel` and `dueAt`. Recipients are the panel and HR-TA |
| `interview.selection.ratified.v1` | `{appId, reqId, ratifiedBy, avg, rounds}`; Pipeline moves the application on |

**Offer** (RCU-OFR):

- Synchronous calls: Config `resolve/matrices/offer` and `resolve/working-days`, Requisition
  `GET /api/v1/requisitions/{reqId}`, Identity's masking map with resource `offer` (fails closed if
  there is none), and Workflow (start, cancel, `GET` instance, `POST approvals/{taskId}/decision`).
- Consumes `pipeline.stage.changed.v1`, `workflow.task.completed.v1`, `bgv.case.initiated.v1`,
  `bgv.cleared.v1`, `bgv.adverse.flagged.v1`, `bgv.resolved.v1` and `candidate.purged.v1`.
- Publishes `offer.submitted.v1`, `offer.approved.v1`, `offer.sent.v1`, `offer.chase_due.v1`,
  `offer.accepted.v1`, `offer.declined.v1`, `offer.expired.v1`, and the new `offer.withdrawn.v1`
  `{offerId, appId, reqId, candidateId, from, reason}`.

**Gateway routes.** `/api/v1/interviews/**` and `/api/v1/offers/**` need a signed-in user. Only two
Offer routes are anonymous, and both use the `public` rate limit:

- `GET /api/v1/offers/letters/**`: the letter link, protected by an HMAC presigned URL.
- `POST /api/v1/offers/esign/callback`: the e-sign provider's callback, protected by an HMAC body
  signature.

**Planned (requested of other threads):**

- Config: an `interview` matrix, and optional extra fields on the offer matrix.
- Identity: an `offer` masking map.
- Bgv: the `bgv.*` events above (PR #24).

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

`offer.accepted.v1.schema.json` (PR #29) carries these fields:

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
