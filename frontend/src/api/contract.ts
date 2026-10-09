// Contract between the UI and the backend. Mirrors the endpoints the .NET services will expose.
import type {
  AcceptInvitationInput,
  AppNotification,
  Application,
  ApplicationStage,
  ApprovalItem,
  AuditEvent,
  AuthSession,
  BgvCase,
  BgvCheckType,
  Candidate,
  CandidateSource,
  DashboardData,
  DemoAccess,
  EmailMessage,
  Invitation,
  InvitationView,
  InviteInput,
  InterviewRound,
  JobDescription,
  JobPosting,
  Offer,
  PasswordResetRequested,
  PipelineCard,
  PublicApplicationInput,
  PublicApplicationResult,
  RegisterCandidateInput,
  RegisterOrganisationInput,
  RegisteredWorkspace,
  Requisition,
  RequisitionInput,
  Role,
  RuleConfig,
  SignInInput,
  SignInResult,
  SsoProvider,
  TenantAppearance,
  User,
  WorkspaceBranding,
} from '../domain/types'

/** Who is calling. The real services will read this from the SSO token (RCU-PLT-001). */
export interface Actor {
  name: string
  role: Role
}

export class ApiError extends Error {
  readonly status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
    this.name = 'ApiError'
  }
}

export interface LogCandidateInput {
  reqId: string
  name: string
  email: string
  phone: string
  experienceYears: number
  source: CandidateSource
  privacyConsent: boolean
}

/**
 * Identity endpoints (RCU-PLT-001). The .NET identity service replaces the mock behind this
 * interface. Errors: 400 validation, 401 bad credentials or expired session/link, 403 RBAC,
 * 404 unknown workspace, 409 duplicate account, 423 locked account, 501 SSO not connected.
 */
export interface AuthApi {
  getWorkspaceBranding(workspace: string): Promise<WorkspaceBranding>
  signIn(input: SignInInput): Promise<SignInResult>
  verifyMfa(challengeId: string, code: string): Promise<AuthSession>
  /** Starts an enterprise SSO redirect. Not connected in the mock. */
  signInWithSso(workspace: string, provider: SsoProvider): Promise<SignInResult>
  /** Restores a session from its token, e.g. after a page reload. */
  getSession(token: string): Promise<AuthSession>
  signOut(token: string): Promise<void>

  registerOrganisation(input: RegisterOrganisationInput): Promise<AuthSession>
  registerCandidate(input: RegisterCandidateInput): Promise<AuthSession>

  requestPasswordReset(workspace: string, email: string): Promise<PasswordResetRequested>
  resetPassword(token: string, password: string): Promise<void>

  listTeam(token: string): Promise<User[]>
  listInvitations(token: string): Promise<Invitation[]>
  inviteStaff(token: string, input: InviteInput): Promise<Invitation>
  getInvitation(inviteToken: string): Promise<InvitationView>
  acceptInvitation(input: AcceptInvitationInput): Promise<AuthSession>

  /** Demo only: persona switcher accounts. The real service returns 404. */
  getDemoAccess(workspace: string): Promise<DemoAccess>
  /** Demo only: sign in as a persona without a password. The real service returns 404. */
  demoSignIn(workspace: string, role: Role): Promise<AuthSession>
}

export interface ApiClient extends AuthApi {
  getRules(): Promise<RuleConfig>
  getDashboard(): Promise<DashboardData>

  listRequisitions(): Promise<Requisition[]>
  createRequisition(actor: Actor, input: RequisitionInput): Promise<Requisition>

  getJobDescription(reqId: string): Promise<JobDescription>
  submitJobDescription(actor: Actor, jd: JobDescription): Promise<JobDescription>

  listPipeline(reqId: string): Promise<PipelineCard[]>
  logCandidate(actor: Actor, input: LogCandidateInput): Promise<PipelineCard>
  moveApplication(actor: Actor, appId: string, to: ApplicationStage): Promise<Application>
  rejectApplication(actor: Actor, appId: string, reason: string): Promise<Application>

  getInterview(appId: string): Promise<InterviewRound>
  getCandidate(id: string): Promise<Candidate>
  submitInterview(actor: Actor, round: InterviewRound): Promise<InterviewRound>

  getBgvCase(appId: string): Promise<BgvCase>
  reportAdverseFinding(
    actor: Actor,
    caseId: string,
    finding: { check: BgvCheckType; description: string; action: 'HoldAndEscalate' | 'SeekClarification' },
  ): Promise<BgvCase>
  releaseOfferAfterBgv(actor: Actor, caseId: string): Promise<void>

  listOffers(): Promise<Offer[]>
  updateOfferComponents(actor: Actor, offerId: string, components: Offer['components']): Promise<Offer>
  submitOffer(actor: Actor, offerId: string): Promise<Offer>
  approveOffer(actor: Actor, offerId: string): Promise<Offer>
  /** RCU-BGV-005: blocked until every applicable BGV check is cleared. */
  sendOffer(actor: Actor, offerId: string): Promise<Offer>
  setOfferOutcome(actor: Actor, offerId: string, outcome: 'Accepted' | 'Declined'): Promise<Offer>

  listApprovals(role: Role): Promise<ApprovalItem[]>
  decideApproval(actor: Actor, id: string, actionId: string, reason?: string): Promise<ApprovalItem>

  listNotifications(role: Role): Promise<AppNotification[]>
  listEmails(role: Role): Promise<EmailMessage[]>
  markNotificationRead(id: string): Promise<void>
  markEmailRead(id: string): Promise<void>
  markAllRead(role: Role): Promise<void>
  simulateEvent(role: Role): Promise<string>

  listJobPostings(): Promise<JobPosting[]>
  submitPublicApplication(input: PublicApplicationInput): Promise<PublicApplicationResult>
  getApplicationStatus(appId: string): Promise<string | null>

  listAudit(): Promise<AuditEvent[]>
}

/**
 * Tenant look and feel, served by the admin portal (GET /api/v1/runtime/{slug}). Resolves `null`
 * when the admin portal is unreachable or doesn't know the tenant, so the portal falls back to its
 * built-in theme instead of failing.
 */
/**
 * Workspaces (tenants) saved by the admin portal's database. `registerWorkspace` is the one write that
 * must reach the server: it stores the tenant in PostgreSQL and creates the owner's account in Keycloak.
 */
export interface WorkspaceApi {
  registerWorkspace(input: RegisterOrganisationInput): Promise<RegisteredWorkspace>
}

export interface AppearanceApi {
  getTenantAppearance(slug: string): Promise<TenantAppearance | null>
}
