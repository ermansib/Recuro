// Domain types. Shapes follow the FRD data model (§9) and use camelCase, which is how
// ASP.NET Core serialises DTOs by default, so the future .NET services can return them as-is.

export type Role = 'hrta' | 'hrhead' | 'mdceo' | 'employee' | 'candidate'

export type Grade = 'E' | 'M1' | 'M3' | 'VP' | 'KMP'

export type ChipTone = 'green' | 'red' | 'amber' | 'slate' | 'navy' | 'gold' | 'purple' | 'teal'

export interface User {
  id: string
  /** Workspace (tenant) the account belongs to. Accounts never span tenants. */
  tenantId: string
  name: string
  initials: string
  role: Role
  title: string
  email: string
  /** One-line description shown in the persona switcher. */
  summary: string
}

/** Kind of organisation running the workspace. It only seeds defaults; every type gets every feature. */
export type OrgType = 'smallBusiness' | 'agency' | 'enterprise'

export type SsoProvider = 'microsoft' | 'google' | 'saml'

export interface TenantConfig {
  id: string
  /** URL-safe workspace id used on the sign-in page and careers site, e.g. `aurora`. */
  slug: string
  name: string
  orgType: OrgType
  legalName: string
  careersTagline: string
  careersIntro: string
  emailDomain: string
  locale: string
  currency: string
  /** Optional overrides of the CSS design tokens in styles/tokens.css. */
  theme?: Record<string, string>
  /** Enterprise sign-in options shown on the login page (RCU-PLT-001). */
  ssoProviders: SsoProvider[]
  /** Roles that must pass a second factor at sign-in (RCU-PLT-001). */
  mfaRoles: Role[]
  /** Idle minutes before the session ends (NFR-01). */
  sessionIdleMinutes: number
}

// ---------- identity & sessions (RCU-PLT-001) ----------

/** What the sign-in page needs to render a tenant's branding before anyone is signed in. */
export type WorkspaceBranding = Pick<
  TenantConfig,
  'slug' | 'name' | 'orgType' | 'careersTagline' | 'theme' | 'ssoProviders' | 'sessionIdleMinutes'
>

export interface AuthSession {
  /** Opaque bearer token. The .NET identity service will issue a JWT here. */
  token: string
  user: User
  tenant: TenantConfig
  issuedAt: string
}

export interface SignInInput {
  workspace: string
  email: string
  password: string
}

export type SignInResult =
  | { status: 'signedIn'; session: AuthSession }
  | {
      status: 'mfaRequired'
      challengeId: string
      /** Masked destination, e.g. `k.***@aurora-demo.example`. */
      deliveredTo: string
      /** Only the mock fills this, so the demo can be used without a mailbox. */
      demoCode?: string
    }

export interface RegisterOrganisationInput {
  orgName: string
  orgType: OrgType
  adminName: string
  /** Role the workspace creator takes. Defaults per org type, the person can change it. */
  adminRole: Exclude<Role, 'candidate' | 'employee'>
  email: string
  password: string
  acceptTerms: boolean
}

export interface RegisterCandidateInput {
  workspace: string
  name: string
  email: string
  password: string
  privacyConsent: boolean
}

export interface PasswordResetRequested {
  /** Masked address the link went to. Always returned, so the form never reveals whether an account exists. */
  deliveredTo: string
  /** Only the mock fills this, so the demo can be used without a mailbox. */
  demoResetPath?: string
}

export type StaffRole = Exclude<Role, 'candidate'>

export interface InviteInput {
  name: string
  email: string
  role: StaffRole
}

export interface Invitation {
  id: string
  tenantId: string
  name: string
  email: string
  role: StaffRole
  invitedBy: string
  createdAt: string
  expiresAt: string
  status: 'Pending' | 'Accepted' | 'Expired'
  /** Path the invitee opens to set a password, e.g. `/invite/abc`. */
  acceptPath: string
}

/** Public view of an invite, shown on the accept page. */
export interface InvitationView {
  workspace: WorkspaceBranding
  name: string
  email: string
  role: StaffRole
  invitedBy: string
}

export interface AcceptInvitationInput {
  token: string
  name: string
  password: string
}

/** Demo-only: personas the switcher can impersonate, and their shared password. */
export interface DemoAccess {
  personas: User[]
  password: string
}

// ---------- rules engine seeds (FRD §5) ----------

export interface DoaRoute {
  grade: Grade
  initiating: string
  recommending: string
  approving: string
  /** Role whose inbox receives the final approval leg. */
  approverRole: Role
  bandLabel: string
  overallTat: { minDays: number; maxDays: number; label: string }
}

export interface OfferMatrixRule {
  levels: Grade[]
  withinBand: { label: string; approverRole: Role }
  deviation: { label: string; approverRole: Role }
}

export interface BgvCheckRule {
  type: BgvCheckType
  label: string
  detail: string
  condition: string
}

export interface RuleConfig {
  version: string
  effectiveFrom: string
  doa: DoaRoute[]
  offerMatrix: OfferMatrixRule[]
  bgvChecks: BgvCheckRule[]
  competencies: { id: string; name: string; hint: string }[]
  departments: string[]
  designations: string[]
  locations: string[]
  reportingManagers: string[]
  sourcingChannels: string[]
  candidateSources: CandidateSource[]
  bands: { label: string; min: number; max: number }[]
}

// ---------- requisitions ----------

export type RequisitionState =
  | 'Draft'
  | 'PendingApproval'
  | 'Approved'
  | 'Sourcing'
  | 'Interviewing'
  | 'Selection'
  | 'BGV'
  | 'Offer'
  | 'Filled'
  | 'Rejected'
  | 'OnHold'
  | 'Cancelled'

export type EmploymentType = 'Permanent' | 'Contractual' | 'OffRoll'
export type RequisitionNature = 'NewPosition' | 'Replacement' | 'Backfill'

export interface RequisitionInput {
  department: string
  designation: string
  grade: Grade | ''
  location: string
  positions: number
  reportingManager: string
  employmentType: EmploymentType
  nature: RequisitionNature
  replacementReason: string
  joiningDate: string
  band: string
  outOfBudget: boolean
  oobJustification: string
  qualifications: string
  sourcingChannels: string[]
}

export interface Requisition extends Omit<RequisitionInput, 'grade'> {
  reqId: string
  grade: Grade
  state: RequisitionState
  owner: string
  raisedAt: string
  targetClosure: string
  route: Pick<DoaRoute, 'initiating' | 'recommending' | 'approving' | 'approverRole'>
  ageDays: number
}

// ---------- job descriptions ----------

export interface JobDescription {
  id: string
  reqId: string
  version: number
  status: 'Draft' | 'Submitted' | 'Approved'
  purpose: string
  responsibilities: string[]
  reportsTo: string
  teamSize: string
  location: string
  minQualification: string
  experience: string
  grade: string
  competencies: string[]
  assessments: string[]
  benchmark: string
  history: { version: number; note: string; by: string; at: string }[]
}

// ---------- candidates & applications ----------

export type CandidateSource =
  | 'IJP'
  | 'Referral'
  | 'Portal'
  | 'Campus'
  | 'Walk-in'
  | 'Consultant'
  | 'Social'
  | 'LinkedIn'
  | 'Direct'

export interface Consent {
  type: 'DataPrivacy' | 'ConflictOfInterest'
  textVersion: string
  at: string
}

export interface Candidate {
  id: string
  name: string
  email: string
  phone: string
  experienceYears: number
  summary: string
  currentCtc: number | null
  expectedCtc: number | null
  noticeDays: number | null
  source: CandidateSource
  sourceRef?: string
  consents: Consent[]
}

export type ApplicationStage =
  | 'Sourced'
  | 'Screened'
  | 'Interview'
  | 'Selection'
  | 'BGV'
  | 'Offer'
  | 'PreBoarding'
  | 'Onboarded'
  | 'Confirmed'
  | 'Rejected'
  | 'Withdrawn'
  | 'Hold'

export interface Application {
  appId: string
  reqId: string
  candidateId: string
  stage: ApplicationStage
  stageHistory: { from: ApplicationStage | null; to: ApplicationStage; by: string; at: string }[]
  note: string
  rejection?: { reason: string; regretDueBy: string; retainUntil: string }
}

export interface PipelineCard {
  application: Application
  candidate: Candidate
}

// ---------- interviews (Annexure B) ----------

export type Recommendation = 'StronglyRecommend' | 'Recommend' | 'Reservations' | 'DoNotRecommend'

export interface InterviewRound {
  id: string
  appId: string
  round: string
  interviewer: string
  mode: 'Telephonic' | 'Video' | 'InPerson'
  scheduledFor: string
  feedbackDueAt: string
  status: 'Scheduled' | 'Draft' | 'Submitted'
  ratings: { competencyId: string; score: number | null; na: boolean; comment: string }[]
  recommendation: Recommendation | null
  justification: string
  submittedAt?: string
  history: { round: string; status: string; tone: ChipTone }[]
}

// ---------- BGV ----------

export type BgvCheckType =
  | 'identity'
  | 'education'
  | 'employment'
  | 'police'
  | 'credit'
  | 'court'
  | 'fitProper'
  | 'social'
  | 'medical'
  | 'coi'

export type BgvCheckStatus = 'Pending' | 'InProgress' | 'Cleared' | 'Flagged' | 'NotApplicable'

export interface BgvCheck {
  type: BgvCheckType
  label: string
  detail: string
  status: BgvCheckStatus
  note: string
  date: string | null
  sensitiveNote?: string
}

export interface BgvCase {
  id: string
  appId: string
  vendor: string
  vendorCaseRef: string
  consentAt: string | null
  initiatedAt: string
  tatDay: number
  tatTotal: number
  checks: BgvCheck[]
  adverse?: { check: BgvCheckType; description: string; action: 'HoldAndEscalate' | 'SeekClarification' }
}

// ---------- offers ----------

export type OfferState =
  | 'Draft'
  | 'PendingApproval'
  | 'Approved'
  | 'Sent'
  | 'Accepted'
  | 'Declined'
  | 'Withdrawn'
  | 'Expired'

export interface Offer {
  id: string
  appId: string
  reqId: string
  candidateName: string
  designation: string
  grade: Grade
  location: string
  reportingManager: string
  joiningDate: string
  probationMonths: number
  components: { fixed: number; variable: number; benefits: number }
  band: { min: number; max: number }
  state: OfferState
  letterVersion: number
  trail: { title: string; detail: string; approved?: boolean }[]
}

// ---------- approvals inbox ----------

export type ApprovalKind = 'MRF' | 'Offer' | 'Deviation' | 'AdverseBgv' | 'Vendor' | 'Kmp' | 'Task'

export interface ApprovalAction {
  id: string
  label: string
  style: 'primary' | 'danger' | 'ghost'
  /** reject = needs a documented reason; query = pauses SLA; resolve = closes the card. */
  effect: 'resolve' | 'reject' | 'query'
  resultText?: string
}

export interface ApprovalItem {
  id: string
  assigneeRole: Role
  kind: ApprovalKind
  tone: 'default' | 'dev' | 'adverse' | 'task'
  title: string
  chip: { text: string; tone: ChipTone }
  meta: string
  route: string
  /** Sensitive amount shown only to roles allowed to see CTC (FRD §3.2). */
  sensitive?: { label: string; value: string }
  entity?: { type: 'Requisition' | 'Offer' | 'BgvCase'; id: string }
  actions: ApprovalAction[]
  createdAt: string
  isNew?: boolean
  decision?: { text: string; by: string; at: string; reason?: string }
}

// ---------- notifications & email (FRD §5.6) ----------

export interface AppNotification {
  id: string
  recipientRole: Role
  icon: string
  title: string
  body: string
  createdAt: string
  unread: boolean
  link?: string
}

export interface EmailMessage {
  id: string
  recipientRole: Role
  tag: string
  from: string
  to: string
  subject: string
  /** Paragraphs of plain text; rendered safely (no raw HTML). */
  paragraphs: string[]
  cta?: string
  signature: string
  createdAt: string
  unread: boolean
}

// ---------- audit (RCU-PLT-004) ----------

export interface AuditEvent {
  id: string
  actor: string
  role: Role
  at: string
  entity: string
  action: string
  before?: string
  after?: string
  reason?: string
  configVersion: string
}

// ---------- public careers ----------

export interface JobPosting {
  id: string
  reqId: string
  title: string
  location: string
  locationFilter: string
  experience: string
  qualification: string
  tags: { text: string; tone: ChipTone }[]
}

export interface PublicApplicationInput {
  postingId: string
  name: string
  email: string
  phone: string
  experienceYears: number | null
  currentCtc: number | null
  expectedCtc: number | null
  noticeDays: number | null
  resumeFileName: string
  privacyConsent: boolean
  coiDeclaration: boolean
}

export interface PublicApplicationResult {
  appId: string
  position: string
  duplicateOf?: string
}

// ---------- dashboard ----------

export interface DashboardData {
  dateLabel: string
  stats: { label: string; value: string; trend: string; tone: '' | 'g' | 'a' | 'r' | 't' }[]
  tatBreaches: { reqId: string; position: string; stage: string; stageTone: ChipTone; escalation: string; link: string }[]
  pipeline: { stage: string; count: number; color: string }[]
  kpis: { name: string; target: string; value: string; status: string; tone: ChipTone; trend: number[] }[]
  upcoming: { day: string; month: string; title: string; detail: string; link: string }[]
  kpiPeriodLabel: string
}
