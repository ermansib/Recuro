// Contract between the UI and the backend. Mirrors the endpoints the .NET services will expose.
import type {
  AppNotification,
  Application,
  ApplicationStage,
  ApprovalItem,
  AuditEvent,
  BgvCase,
  BgvCheckType,
  Candidate,
  CandidateSource,
  DashboardData,
  EmailMessage,
  InterviewRound,
  JobDescription,
  JobPosting,
  Offer,
  PipelineCard,
  PublicApplicationInput,
  PublicApplicationResult,
  Requisition,
  RequisitionInput,
  Role,
  RuleConfig,
  User,
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

export interface ApiClient {
  getUsers(): Promise<User[]>
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
