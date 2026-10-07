# CLAUDE.md

Guidance for Claude (and humans) working in this repository.

## What Recuro is

Recuro ("Recruitment, uninterrupted.") is a web portal for **recruitment and selection**: manpower
requisitions, approvals, job descriptions, candidate pipeline, interviews, background verification,
offers, onboarding, vendors, reporting, an internal careers portal and a public careers site.

The long-term vision is a **domain-neutral recruitment CRM**. Build every feature so it can grow into
that: anything industry- or tenant-specific (departments, grades, bands, checks, approval routes, TATs,
branding) is **configuration, not code**.

Recuro is a **white-label, multi-tenant** product. The same portal is used by in-house HR teams and by
staffing agencies, for many companies, domains and areas of recruitment. "Recuro" is the product
brand; each tenant brings its own name, careers copy, theme, locale and rules. The demo tenant is the
fictional "Aurora Housing Finance" (`src/mocks/data/tenant.json`). Never hard-code a tenant's name,
policy wording or branding in components.

## Stack and build order

1. **Frontend first (current phase):** React.js + TypeScript. Not Next.js.
2. **Backend later:** .NET Core microservices. Until they exist, the frontend talks to a mock/in-memory
   API layer that mirrors the contracts the services will expose, so swapping in real services is a
   change in one place.

Rules that follow from this:

- TypeScript everywhere, `strict` mode on. No `any` without a comment explaining why.
- Keep all data access behind a typed API client module (one place per domain, e.g. `requisitions`,
  `candidates`, `offers`). Components never call `fetch` directly or import mock data directly.
- Model domain types (Requisition, Application, Offer, BGVCase, ...) from the data model in the FRD
  (§9) so they map cleanly onto future .NET DTOs.
- State transitions follow the FRD state machines (§6). Encode them as data (allowed-transition maps),
  not scattered `if`s, so the backend can reuse the same rules.
- Identity goes through the `AuthApi` part of the `ApiClient` (sign-in, MFA, SSO, sign-up, reset,
  invites). The mock in `api/mock/identity.ts` stands in for the .NET identity service; the demo
  persona switcher is dev-only (`config/features.ts`).
- Role-based UI (hiding buttons, masking CTC/PII) is a convenience only. The real enforcement will live
  in the .NET services; never assume the UI is the security boundary.

Current scope: **P0 (MVP) stories only**, UI only. Data comes from static JSON mocks shaped like the
future .NET DTOs. P1 screens (Onboarding, Vendors, Reports, Internal Careers) are placeholders.

## Tooling and layout

Monorepo: `frontend/` now, `services/` (.NET Core) later.

| Concern | Choice |
|---|---|
| Bundler / dev server | Vite |
| Routing | react-router-dom |
| Server state | @tanstack/react-query (hooks in `src/api/hooks.ts`) |
| i18n | i18next + react-i18next, English only for now, one JSON per feature in `src/i18n/en` |
| Styling | Plain CSS ported from the `HR.html` prototype (`src/styles`). No component library. |
| Tests | Vitest + jsdom + Testing Library |
| Lint | oxlint; TypeScript `strict` + `noUncheckedIndexedAccess` |

```
frontend/src/
  api/contract.ts     ApiClient interface: the one seam to the backend
  api/client.ts       picks the implementation (mock today, HTTP later)
  api/mock/           in-memory mock that enforces RBAC, state machines and rules
  mocks/data/*.json   static seed data (camelCase, like ASP.NET Core DTOs)
  domain/             types, state machines, pure rule functions (DOA, offer matrix, BGV gate)
  auth/               session lifecycle (sign-in, restore, idle timeout, sign-out) and the RBAC matrix
  config/tenant.ts    white-label tenant config and theme
  components/         layout, topbar, notification drawer, shared UI
  features/<screen>/  one folder per screen
```

The mock returns the same errors the services should (400 validation, 403 RBAC, 409 illegal
transition or locked state) and writes audit events, notifications and emails. When the .NET services
exist, add an HTTP `ApiClient` and switch `api` in `src/api/client.ts`; components don't change.

Commands (run in `frontend/`): `npm install`, `npm run dev`, `npm test`, `npm run lint`,
`npm run typecheck`, `npm run build`.

## Source documents

These are the requirements. Read them before building a screen.

- **`HR.html`** — interactive hi-fi prototype. It is the UX contract: layout, screens, flows and the
  role switcher. Where a user story explicitly differs, the story wins.
- **`UserStories.html`** — FRD & User Stories Handoff Pack (RCU-FRD-001 v1.0): 14 modules, 75 stories
  with Gherkin acceptance criteria, rules matrices, state machines, NFRs, data model and phasing.

Both are in the project's shared files. Story IDs (`RCU-MRF-001`, ...) and screen IDs (`S-02`, ...)
should be referenced in commits, PRs and code comments where useful.

## Personas

| Persona | Role key | Main screens |
|---|---|---|
| HR-TA (Talent Acquisition) | `hrta` | Dashboard, MRF, JD, Pipeline, Assessment, BGV, Offer, Onboarding |
| HR Head | `hrhead` | Approvals, Vendors, Reports |
| MD/CEO | `mdceo` | Approvals, Reports (board view); CTC/PII masked |
| Employee | `employee` | Internal Careers (IJP, referrals) |
| Candidate | `candidate` | Public Careers site |

The RBAC matrix in FRD §3.2 is authoritative.

## Screens (prototype IDs)

| ID | Screen | Story prefix |
|---|---|---|
| — | Platform: auth, RBAC, audit, config | `RCU-PLT` |
| S-01 | Dashboard | `RCU-DSH` |
| S-02 | Manpower Requisition (MRF) wizard | `RCU-MRF` |
| S-04 | Approvals inbox | `RCU-APR` |
| S-05 | Job Description builder | `RCU-JD` |
| S-06 | Candidate pipeline (kanban) | `RCU-PIP` |
| S-09 | Interview scheduling & assessment (Annexure B) | `RCU-ASM` |
| S-11 | Background verification | `RCU-BGV` |
| S-12 | Offer management | `RCU-OFF` |
| S-13 | Pre-boarding, onboarding & probation | `RCU-ONB` |
| S-14 | Vendor & consultant management | `RCU-VEN` |
| S-15 | Reports & KPIs | `RCU-RPT` |
| S-16 | Employee portal: IJP & referrals | `RCU-EMP` |
| S-17 | Public career site | `RCU-CAR` |
| — | Notification & email center | `RCU-NTF` |

Build in priority order within each module: **P0 (MVP) → P1 → P2**, per FRD §10.

## Domain glossary

- **MRF** — Manpower Requisition Form; approved demand that unlocks sourcing.
- **DOA** — Delegation of Authority; grade-based approval route (E, M1, M3, VP, KMP).
- **TAT** — Turnaround time; stage SLAs with escalation on breach.
- **BGV** — Background verification, run by empanelled vendors.
- **IJP** — Internal Job Posting; 5-working-day internal window before external sourcing.
- **KMP** — Key Managerial Personnel; senior hires with Fit & Proper governance.
- **OOB** — Out-of-budget requisition; needs justification and an extra approval leg.
- **COI** — Conflict-of-interest declaration.

## Core state machines (FRD §6)

- **Requisition:** Draft → Pending Approval → Approved → Sourcing → Interviewing → Selection → BGV →
  Offer → Filled; side states Rejected, On Hold, Cancelled.
- **Application:** Sourced → Screened → Interview → Selection → BGV → Offer → Pre-boarding →
  Onboarded → Confirmed; side states Rejected, Withdrawn, Hold.
- **BGV check:** Pending → In Progress → Cleared, or Flagged → Under Review → Resolved–Cleared /
  Resolved–Adverse.
- **Offer:** Draft → Pending Approval → Approved → Sent → Accepted; side states Declined, Withdrawn,
  Expired.

## Frontend conventions

- Design tokens come from the Recuro prototype's `:root` variables and live in
  `src/styles/tokens.css` (navy `#1E2A5E`, cyan `#22D3EE`, violet accent `#7C3AED`, background
  `#F4F6FB`, radius 12px). Tenants override them through `theme` in their config (RCU-PLT-006).
- Role visibility is decided in React with `can(role, capability)`, not with CSS selectors.
- No hard-coded user-facing strings in components; externalise them so i18n (NFR-07) is possible.
- Accessibility target is WCAG 2.1 AA (NFR-06): labelled forms, keyboard-operable kanban, visible
  focus, contrast ≥ 4.5:1.
- Responsive from 360px to 1920px; latest two versions of Chrome, Edge, Firefox, Safari (NFR-08).
- Dates, numbers and currency are locale-aware; working-day calculations live in one utility.
