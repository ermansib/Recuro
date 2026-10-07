// Loads the static JSON seed (shaped like the future .NET API responses) into a fresh,
// mutable in-memory database.
import applications from '../../mocks/data/applications.json'
import approvals from '../../mocks/data/approvals.json'
import bgvCases from '../../mocks/data/bgv-cases.json'
import candidates from '../../mocks/data/candidates.json'
import dashboard from '../../mocks/data/dashboard.json'
import emails from '../../mocks/data/emails.json'
import interviews from '../../mocks/data/interviews.json'
import jobDescriptions from '../../mocks/data/job-descriptions.json'
import jobPostings from '../../mocks/data/job-postings.json'
import notifications from '../../mocks/data/notifications.json'
import offers from '../../mocks/data/offers.json'
import requisitions from '../../mocks/data/requisitions.json'
import rules from '../../mocks/data/rules.json'
import tenant from '../../mocks/data/tenant.json'
import users from '../../mocks/data/users.json'
import type {
  AppNotification,
  Application,
  ApprovalItem,
  AuditEvent,
  BgvCase,
  Candidate,
  DashboardData,
  EmailMessage,
  InterviewRound,
  JobDescription,
  JobPosting,
  Offer,
  Requisition,
  RuleConfig,
  TenantConfig,
  User,
} from '../../domain/types'

export interface MockDb {
  tenant: TenantConfig
  users: User[]
  rules: RuleConfig
  dashboard: DashboardData
  requisitions: Requisition[]
  jobDescriptions: JobDescription[]
  candidates: Candidate[]
  applications: Application[]
  interviews: InterviewRound[]
  bgvCases: BgvCase[]
  offers: Offer[]
  approvals: ApprovalItem[]
  notifications: AppNotification[]
  emails: EmailMessage[]
  jobPostings: JobPosting[]
  audit: AuditEvent[]
}

// JSON imports are typed structurally by TypeScript; the casts assert they match the DTOs.
// src/mocks/seed.test.ts checks the invariants the casts rely on.
export function loadSeed(): MockDb {
  return structuredClone({
    tenant: tenant as TenantConfig,
    users: users as User[],
    rules: rules as RuleConfig,
    dashboard: dashboard as DashboardData,
    requisitions: requisitions as Requisition[],
    jobDescriptions: jobDescriptions as JobDescription[],
    candidates: candidates as Candidate[],
    applications: applications as Application[],
    interviews: interviews as InterviewRound[],
    bgvCases: bgvCases as BgvCase[],
    offers: offers as Offer[],
    approvals: approvals as ApprovalItem[],
    notifications: notifications as AppNotification[],
    emails: emails as EmailMessage[],
    jobPostings: jobPostings as JobPosting[],
    audit: [],
  })
}
