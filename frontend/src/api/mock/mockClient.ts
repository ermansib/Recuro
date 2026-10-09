// In-memory mock of the future .NET services, seeded from static JSON. It applies the same
// rules the services must enforce (RBAC, state machines, gates) so the UI behaves realistically.
// State lives for the browser session only.
import { can } from '../../auth/permissions'
import { applicationTransitions, canTransition } from '../../domain/stateMachines'
import {
  assessmentAverage,
  offerRouting,
  resolveDoa,
  summariseBgv,
  targetClosureDate,
} from '../../domain/rules'
import type {
  AppNotification,
  Application,
  ApplicationStage,
  ApprovalItem,
  AuditEvent,
  Candidate,
  EmailMessage,
  Offer,
  PipelineCard,
  Requisition,
  RequisitionState,
  Role,
} from '../../domain/types'
import { addWorkingDays, toIsoDate } from '../../utils/workingDays'
import { ApiError, type Actor, type ApiClient, type WorkspaceApi } from '../contract'
import { createIdentityMock } from './identity'
import { DEMO_PASSWORD, loadSeed, type MockDb } from './seed'

const LATENCY_MS = import.meta.env.MODE === 'test' ? 0 : 120

const SOURCING_OPEN: RequisitionState[] = ['Approved', 'Sourcing', 'Interviewing', 'Selection', 'BGV', 'Offer']

/**
 * `workspaces` is the server that saves new workspaces. Without it (tests, offline demos) sign-up only
 * creates the workspace in this browser.
 */
export function createMockClient(seed: () => MockDb = loadSeed, workspaces?: WorkspaceApi): ApiClient {
  const db = seed()
  let seq = 1000

  const nextId = (prefix: string) => `${prefix}-${++seq}`
  const now = () => new Date().toISOString()
  const respond = <T>(value: T): Promise<T> =>
    new Promise((resolve) => setTimeout(() => resolve(structuredClone(value)), LATENCY_MS))
  const fail = (status: number, message: string): Promise<never> =>
    new Promise((_, reject) => setTimeout(() => reject(new ApiError(status, message)), LATENCY_MS))

  function audit(actor: Actor, entity: string, action: string, extra: Partial<AuditEvent> = {}) {
    db.audit.push({
      id: nextId('aud'),
      actor: actor.name,
      role: actor.role,
      at: now(),
      entity,
      action,
      configVersion: db.rules.version,
      ...extra,
    })
  }

  function notify(role: Role, n: Omit<AppNotification, 'id' | 'recipientRole' | 'createdAt' | 'unread'>) {
    db.notifications.unshift({ id: nextId('n'), recipientRole: role, createdAt: now(), unread: true, ...n })
  }

  function email(role: Role, e: Omit<EmailMessage, 'id' | 'recipientRole' | 'createdAt' | 'unread' | 'from'> & { from?: string }) {
    db.emails.unshift({
      id: nextId('e'),
      recipientRole: role,
      createdAt: now(),
      unread: true,
      from: e.from ?? `Recuro for ${db.tenant.name} <no-reply@${db.tenant.emailDomain}>`,
      ...e,
    })
  }

  const systemSignature = () => `${db.tenant.name} Recruitment (automated) · decision is audit-logged`
  const roleMailbox = (role: Role) => {
    const user = db.users.find((u) => u.role === role)
    return user?.email ?? `${role}@${db.tenant.emailDomain}`
  }
  const roleName = (role: Role) => db.users.find((u) => u.role === role)?.name ?? role

  function findApplication(appId: string): Application {
    const app = db.applications.find((a) => a.appId === appId)
    if (!app) throw new ApiError(404, `Application ${appId} not found`)
    return app
  }

  function candidateOf(app: Application): Candidate {
    const c = db.candidates.find((x) => x.id === app.candidateId)
    if (!c) throw new ApiError(404, `Candidate ${app.candidateId} not found`)
    return c
  }

  function nextNumber(prefix: string, ids: string[]): string {
    const year = new Date().getFullYear()
    const max = ids
      .map((id) => Number(id.split('-')[2]))
      .filter((n) => !Number.isNaN(n))
      .reduce((a, b) => Math.max(a, b), 0)
    return `${prefix}-${year}-${String(max + 1).padStart(4, '0')}`
  }

  function moveStage(actor: Actor, app: Application, to: ApplicationStage) {
    if (!canTransition(applicationTransitions, app.stage, to)) {
      throw new ApiError(409, `Cannot move from ${app.stage} to ${to}`)
    }
    const from = app.stage
    app.stageHistory.push({ from, to, by: actor.name, at: now() })
    app.stage = to
    audit(actor, `Application/${app.appId}`, 'STAGE_MOVE', { before: from, after: to })
  }

  function setRequisitionState(actor: Actor, req: Requisition, to: RequisitionState, reason?: string) {
    const before = req.state
    req.state = to
    audit(actor, `Requisition/${req.reqId}`, 'STATE_CHANGE', { before, after: to, reason })
  }

  // Wraps a synchronous mutation so thrown ApiErrors become rejected promises.
  function run<T>(fn: () => T): Promise<T> {
    try {
      return respond(fn())
    } catch (e) {
      if (e instanceof ApiError) return fail(e.status, e.message)
      throw e
    }
  }

  function runAsync<T>(fn: () => T | Promise<T>): Promise<T> {
    return Promise.resolve()
      .then(fn)
      .then(respond, (e: unknown) => (e instanceof ApiError ? fail(e.status, e.message) : Promise.reject(e)))
  }

  const identity = createIdentityMock({
    db,
    workspaces,
    demoPassword: DEMO_PASSWORD,
    audit,
    run: runAsync,
    sendEmail: (role, to, tenant, subject, paragraphs, cta) =>
      email(role, {
        tag: 'Account',
        from: `Recuro for ${tenant.name} <no-reply@${tenant.emailDomain}>`,
        to,
        subject,
        paragraphs,
        cta,
        signature: `${tenant.name} · sent by Recuro`,
      }),
  })

  function requireCapability(actor: Actor, ok: boolean, message: string) {
    if (!ok) {
      audit(actor, 'access', 'ACCESS_DENIED', { reason: message })
      throw new ApiError(403, message)
    }
  }

  return {
    ...identity,
    getRules: () => respond(db.rules),
    getDashboard: () => respond(db.dashboard),

    listRequisitions: () => respond(db.requisitions),

    createRequisition: (actor, input) =>
      run(() => {
        requireCapability(actor, can(actor.role, 'mrf.raise'), 'Only HR-TA raises MRFs.')
        const missing = (
          [
            ['department', input.department],
            ['designation', input.designation],
            ['grade', input.grade],
            ['location', input.location],
            ['reportingManager', input.reportingManager],
            ['joiningDate', input.joiningDate],
            ['band', input.band],
            ['qualifications', input.qualifications.trim()],
          ] as const
        )
          .filter(([, v]) => !v)
          .map(([k]) => k)
        if (missing.length) throw new ApiError(400, `Missing required fields: ${missing.join(', ')}`)
        if (input.positions < 1) throw new ApiError(400, 'At least one position is required')
        if (input.outOfBudget && !input.oobJustification.trim()) {
          throw new ApiError(400, 'Out-of-budget requisitions need a justification')
        }
        if (input.nature !== 'NewPosition' && !input.replacementReason.trim()) {
          throw new ApiError(400, 'Replacement or backfill reason is required')
        }
        if (!input.grade) throw new ApiError(400, 'Grade is required')
        const doa = resolveDoa(db.rules, input.grade)
        // RCU-MRF-003: out-of-budget appends the elevated approver (MD/CEO).
        const approverRole: Role = input.outOfBudget ? 'mdceo' : doa.approverRole
        const approving = input.outOfBudget && doa.approverRole !== 'mdceo' ? `${doa.approving} + MD/CEO (OOB)` : doa.approving
        const req: Requisition = {
          ...input,
          grade: input.grade,
          reqId: nextNumber('REQ', db.requisitions.map((r) => r.reqId)),
          state: 'PendingApproval',
          owner: actor.name,
          raisedAt: toIsoDate(new Date()),
          targetClosure: targetClosureDate(doa, new Date()),
          route: { initiating: doa.initiating, recommending: doa.recommending, approving, approverRole },
          ageDays: 0,
        }
        db.requisitions.unshift(req)
        audit(actor, `Requisition/${req.reqId}`, 'CREATED', { after: 'PendingApproval' })
        db.approvals.unshift({
          id: nextId('apr'),
          assigneeRole: approverRole,
          kind: 'MRF',
          tone: input.outOfBudget || input.grade === 'VP' || input.grade === 'KMP' ? 'dev' : 'default',
          title: `MRF Approval — ${req.reqId}`,
          chip: { text: '⏱ SLA 2d', tone: 'amber' },
          meta: `${req.designation} · ${req.grade} · ${req.location} · ${req.positions} position(s) · ${
            input.outOfBudget ? '⚠ Out-of-budget' : 'In-budget ✓'
          }`,
          sensitive: { label: 'Band', value: req.band },
          route: `Route: ${doa.initiating} → ${doa.recommending} → ${approving}`,
          entity: { type: 'Requisition', id: req.reqId },
          actions: [
            { id: 'approve', label: 'Approve', style: 'primary', effect: 'resolve', resultText: 'MRF approved — sourcing unlocked' },
            { id: 'reject', label: 'Reject', style: 'danger', effect: 'reject' },
            { id: 'query', label: 'Query / RFI', style: 'ghost', effect: 'query' },
          ],
          createdAt: now(),
          isNew: true,
        })
        notify(approverRole, {
          icon: '📋',
          title: `MRF approval requested — ${req.reqId}`,
          body: `${req.designation} · ${req.grade} · SLA 2 working days.`,
          link: '/approvals',
        })
        email(approverRole, {
          tag: 'Approval',
          to: roleMailbox(approverRole),
          subject: `[Action Required] MRF ${req.reqId} — ${req.designation} (SLA: 2 working days)`,
          paragraphs: [
            `Dear ${roleName(approverRole)},`,
            `A Manpower Requisition requires your approval under the Delegation of Authority: ${req.designation} (${req.grade}), ${req.location}.`,
            input.outOfBudget
              ? `Budget: out-of-budget — justification attached: ${input.oobJustification}`
              : 'Budget: in the approved manpower plan ✓',
            'Sourcing is locked until your decision. Act via the Approvals inbox.',
          ],
          signature: systemSignature(),
        })
        return req
      }),

    getJobDescription: (reqId) => {
      const jd = db.jobDescriptions.find((j) => j.reqId === reqId)
      return jd ? respond(jd) : fail(404, `No job description for ${reqId}`)
    },

    submitJobDescription: (actor, jd) =>
      run(() => {
        requireCapability(actor, can(actor.role, 'jd.edit'), 'JD drafts are owned by HOD / HR-TA.')
        const responsibilities = jd.responsibilities.map((r) => r.trim()).filter(Boolean)
        if (!jd.purpose.trim()) throw new ApiError(400, 'Role purpose is required')
        if (responsibilities.length < 2) throw new ApiError(400, 'Add at least 2 key responsibilities')
        if (responsibilities.length > 6) throw new ApiError(400, 'At most 6 key responsibilities')
        if (jd.competencies.length < 3) throw new ApiError(400, 'Select at least 3 competencies')
        if (jd.assessments.length < 1) throw new ApiError(400, 'Select at least one assessment')
        const idx = db.jobDescriptions.findIndex((j) => j.id === jd.id)
        const existing = db.jobDescriptions[idx]
        if (!existing) throw new ApiError(404, 'Job description not found')
        // RCU-JD-004: each submit freezes a new immutable version.
        const version = existing.version + 1
        const next = {
          ...jd,
          responsibilities,
          version,
          status: 'Submitted' as const,
          history: [{ version, note: 'submitted for grade benchmark & approval', by: actor.name, at: toIsoDate(new Date()) }, ...existing.history],
        }
        db.jobDescriptions[idx] = next
        audit(actor, `JobDescription/${jd.id}`, 'VERSION_FROZEN', { after: `v${version}` })
        return next
      }),

    listPipeline: (reqId) =>
      respond(
        db.applications
          .filter((a) => a.reqId === reqId)
          .map((application): PipelineCard => ({ application, candidate: candidateOf(application) })),
      ),

    logCandidate: (actor, input) =>
      run(() => {
        requireCapability(actor, can(actor.role, 'candidate.log'), 'Only HR-TA logs candidates.')
        const req = db.requisitions.find((r) => r.reqId === input.reqId)
        if (!req) throw new ApiError(404, 'Requisition not found')
        // RCU-MRF-006: sourcing is hard-locked until the MRF is approved.
        if (!SOURCING_OPEN.includes(req.state)) {
          throw new ApiError(409, `Sourcing is locked: ${req.reqId} is ${req.state}, not Approved`)
        }
        if (!input.name.trim() || !input.email.trim()) throw new ApiError(400, 'Name and email are required')
        if (!input.privacyConsent) throw new ApiError(400, 'Privacy consent must be captured')
        const dupe = db.candidates.find(
          (c) => c.email.toLowerCase() === input.email.trim().toLowerCase() || (input.phone && c.phone === input.phone),
        )
        if (dupe) throw new ApiError(409, `Possible duplicate of ${dupe.name} (${dupe.email})`)
        const candidate: Candidate = {
          id: nextId('cand'),
          name: input.name.trim(),
          email: input.email.trim(),
          phone: input.phone.trim(),
          experienceYears: input.experienceYears,
          summary: `${input.experienceYears} yrs`,
          currentCtc: null,
          expectedCtc: null,
          noticeDays: null,
          source: input.source,
          consents: [{ type: 'DataPrivacy', textVersion: 'v1', at: now() }],
        }
        const application: Application = {
          appId: nextNumber('APP', db.applications.map((a) => a.appId)),
          reqId: req.reqId,
          candidateId: candidate.id,
          stage: 'Sourced',
          stageHistory: [{ from: null, to: 'Sourced', by: actor.name, at: now() }],
          note: `Logged by ${actor.name}`,
        }
        db.candidates.push(candidate)
        db.applications.push(application)
        if (req.state === 'Approved') setRequisitionState(actor, req, 'Sourcing')
        audit(actor, `Application/${application.appId}`, 'CREATED', { after: 'Sourced' })
        return { application, candidate }
      }),

    moveApplication: (actor, appId, to) =>
      run(() => {
        requireCapability(actor, can(actor.role, 'pipeline.move'), 'Read-only — stage changes are restricted to HR roles.')
        const app = findApplication(appId)
        if (to === 'Rejected') throw new ApiError(400, 'Use reject with a documented reason')
        moveStage(actor, app, to)
        return app
      }),

    rejectApplication: (actor, appId, reason) =>
      run(() => {
        requireCapability(actor, can(actor.role, 'pipeline.move'), 'Read-only — stage changes are restricted to HR roles.')
        if (!reason.trim()) throw new ApiError(400, 'A rejection reason is mandatory')
        const app = findApplication(appId)
        moveStage(actor, app, 'Rejected')
        const today = new Date()
        const retain = new Date(today)
        retain.setFullYear(retain.getFullYear() + 1)
        app.rejection = {
          reason: reason.trim(),
          regretDueBy: toIsoDate(addWorkingDays(today, 3)),
          retainUntil: toIsoDate(retain),
        }
        app.note = `Rejected — ${reason.trim()}`
        const candidate = candidateOf(app)
        // RCU-PIP-005 / RCU-CAR-005: regret email, no reason disclosed, within 3 working days.
        email('candidate', {
          tag: 'Decision',
          from: `${db.tenant.name} — Careers <careers@${db.tenant.emailDomain}>`,
          to: candidate.email,
          subject: `Update on your application ${app.appId}`,
          paragraphs: [
            `Dear ${candidate.name},`,
            `Thank you for your interest in ${db.tenant.name}. After careful consideration, we will not be taking your application forward for this role.`,
            'We will keep your profile on file for one year in line with our data retention policy, after which it is deleted.',
          ],
          signature: `Talent Acquisition Team · ${db.tenant.name}`,
        })
        return app
      }),

    getInterview: (appId) => {
      const round = db.interviews.find((i) => i.appId === appId)
      return round ? respond(round) : fail(404, `No interview for ${appId}`)
    },

    getCandidate: (id) => {
      const c = db.candidates.find((x) => x.id === id)
      return c ? respond(c) : fail(404, 'Candidate not found')
    },

    submitInterview: (actor, round) =>
      run(() => {
        requireCapability(actor, can(actor.role, 'assessment.submit'), 'Feedback is owned by the assigned interviewer.')
        const idx = db.interviews.findIndex((i) => i.id === round.id)
        const existing = db.interviews[idx]
        if (!existing) throw new ApiError(404, 'Interview not found')
        // RCU-ASM-005: submitted feedback is immutable.
        if (existing.status === 'Submitted') throw new ApiError(409, 'Feedback already submitted and locked')
        if (!round.recommendation) throw new ApiError(400, 'Select an overall recommendation')
        if (!round.justification.trim()) throw new ApiError(400, 'Justification is required')
        if (round.ratings.some((r) => !r.na && r.score === null)) throw new ApiError(400, 'Rate every competency or mark it N/A')
        const next = { ...round, status: 'Submitted' as const, submittedAt: now() }
        db.interviews[idx] = next
        const avg = assessmentAverage(next.ratings)
        audit(actor, `Interview/${round.id}`, 'FEEDBACK_SUBMITTED', { after: `avg ${avg ?? '—'}` })
        notify('hrta', {
          icon: '📝',
          title: `Assessment submitted — ${round.round}`,
          body: `Average ${avg ?? '—'}/5 · fed to the Selection Summary.`,
          link: '/pipeline',
        })
        return next
      }),

    getBgvCase: (appId) => {
      const c = db.bgvCases.find((b) => b.appId === appId)
      return c ? respond(c) : fail(404, `No BGV case for ${appId}`)
    },

    reportAdverseFinding: (actor, caseId, finding) =>
      run(() => {
        requireCapability(actor, can(actor.role, 'bgv.reportAdverse'), 'Adverse findings are reported by HR-TA.')
        if (!finding.description.trim()) throw new ApiError(400, 'Finding description is required')
        const bgv = db.bgvCases.find((b) => b.id === caseId)
        if (!bgv) throw new ApiError(404, 'BGV case not found')
        const check = bgv.checks.find((c) => c.type === finding.check)
        if (!check) throw new ApiError(404, 'Check not found')
        if (check.status === 'NotApplicable' || check.status === 'Cleared') {
          throw new ApiError(409, `${check.label} is ${check.status} and cannot be flagged`)
        }
        const before = check.status
        check.status = 'Flagged'
        check.note = finding.description.trim()
        bgv.adverse = { ...finding, description: finding.description.trim() }
        audit(actor, `BgvCase/${bgv.id}`, 'BGV_FLAGGED', { before, after: 'Flagged', reason: check.note })
        const candidate = candidateOf(findApplication(bgv.appId))
        const base = {
          tone: 'adverse' as const,
          kind: 'AdverseBgv' as const,
          chip: { text: check.label, tone: 'red' as const },
          entity: { type: 'BgvCase' as const, id: bgv.id },
          createdAt: now(),
          isNew: true,
        }
        db.approvals.unshift(
          {
            ...base,
            id: nextId('apr'),
            assigneeRole: 'hrhead',
            title: `⚑ Adverse BGV — ${candidate.name} (${findApplication(bgv.appId).reqId})`,
            meta: check.note,
            route: 'First escalation: HR Head + Compliance → final: MD/CEO. Offer ON HOLD.',
            actions: [
              { id: 'escalate', label: 'Escalate to MD/CEO', style: 'danger', effect: 'resolve', resultText: 'Escalated to MD/CEO with Compliance note' },
              { id: 'clarify', label: 'Seek Vendor Clarification', style: 'ghost', effect: 'query' },
            ],
          },
          {
            ...base,
            id: nextId('apr'),
            assigneeRole: 'mdceo',
            title: `⚑ Final Escalation — Adverse BGV · ${candidate.name}`,
            meta: `Vendor finding: ${check.note}`,
            route: 'Final authority: MD/CEO. Decision must be documented.',
            actions: [
              { id: 'rescind', label: 'Accept — Rescind', style: 'danger', effect: 'reject' },
              { id: 'override', label: 'Override — Proceed', style: 'ghost', effect: 'reject' },
            ],
          },
        )
        notify('hrhead', { icon: '⚑', title: `Adverse BGV — ${candidate.name}`, body: `${check.label} · offer on hold · action required.`, link: '/approvals' })
        notify('mdceo', { icon: '⚑', title: 'Final escalation — Adverse BGV', body: `${check.label} · vendor finding needs a decision.`, link: '/approvals' })
        email('hrhead', {
          tag: 'Compliance',
          to: roleMailbox('hrhead'),
          subject: `⚑ Adverse BGV — ${candidate.name} (first escalation)`,
          paragraphs: [`The vendor reported an adverse finding on ${check.label}: ${check.note}`, 'The offer is ON HOLD pending your decision.'],
          signature: systemSignature(),
        })
        return bgv
      }),

    releaseOfferAfterBgv: (actor, caseId) =>
      run(() => {
        requireCapability(actor, can(actor.role, 'offer.release'), 'Release requires HR authorisation.')
        const bgv = db.bgvCases.find((b) => b.id === caseId)
        if (!bgv) throw new ApiError(404, 'BGV case not found')
        const s = summariseBgv(bgv)
        if (!s.releaseAllowed) {
          throw new ApiError(
            409,
            `Cannot release — ${s.inProgress} in progress, ${s.pending} pending, ${s.flagged} flagged: ${s.blocking.join(', ')}`,
          )
        }
        const app = findApplication(bgv.appId)
        if (app.stage === 'BGV') moveStage(actor, app, 'Offer')
        audit(actor, `BgvCase/${bgv.id}`, 'BGV_CLEARED')
      }),

    listOffers: () => respond(db.offers),

    updateOfferComponents: (actor, offerId, components) =>
      run(() => {
        requireCapability(actor, can(actor.role, 'offer.edit'), 'Only HR-TA edits the CTC structure.')
        const offer = findOffer(offerId)
        if (offer.state !== 'Draft' && offer.state !== 'PendingApproval') {
          throw new ApiError(409, `Offer is ${offer.state} and can no longer be edited`)
        }
        if (Object.values(components).some((v) => v < 0 || Number.isNaN(v))) {
          throw new ApiError(400, 'CTC components must be zero or more')
        }
        const before = offerRouting(db.rules, offer.grade, offer.components, offer.band).total
        offer.components = components
        const after = offerRouting(db.rules, offer.grade, components, offer.band).total
        if (before !== after) {
          offer.trail.unshift({ title: `CTC revised to ₹${after.toFixed(1)}L`, detail: `${actor.name} · ${new Date().toLocaleString()}` })
          audit(actor, `Offer/${offer.id}`, 'CTC_REVISED', { before: String(before), after: String(after) })
        }
        return offer
      }),

    submitOffer: (actor, offerId) =>
      run(() => {
        requireCapability(actor, can(actor.role, 'offer.edit'), 'Only HR-TA submits offers for approval.')
        const offer = findOffer(offerId)
        if (offer.state !== 'Draft' && offer.state !== 'PendingApproval') {
          throw new ApiError(409, `Offer is already ${offer.state}`)
        }
        const routing = offerRouting(db.rules, offer.grade, offer.components, offer.band)
        offer.state = 'PendingApproval'
        offer.trail.unshift({ title: `Sent for approval — ${routing.label}`, detail: `${actor.name} · just now` })
        // RCU-OFF-003: one approval task for the resolved authority only.
        db.approvals = db.approvals.filter((a) => !(a.entity?.type === 'Offer' && a.entity.id === offer.id && !a.decision))
        db.approvals.unshift({
          id: nextId('apr'),
          assigneeRole: routing.approverRole,
          kind: routing.withinBand ? 'Offer' : 'Deviation',
          tone: routing.withinBand ? 'default' : 'dev',
          title: `${routing.withinBand ? 'Offer Sign-off (Within Band)' : 'CTC Deviation'} — ${offer.candidateName}`,
          chip: routing.withinBand
            ? { text: 'Annexure D', tone: 'navy' }
            : { text: `+₹${routing.deviation.toFixed(1)}L over band`, tone: 'red' },
          meta: `${offer.designation} · ${offer.grade} · band ₹${offer.band.min}–${offer.band.max}L`,
          sensitive: { label: 'CTC', value: `₹${routing.total.toFixed(1)}L` },
          route: routing.label,
          entity: { type: 'Offer', id: offer.id },
          actions: [
            { id: 'approve', label: routing.withinBand ? 'Sign-off' : 'Approve Deviation', style: 'primary', effect: 'resolve', resultText: 'Offer approved (Annexure D)' },
            { id: 'decline', label: 'Return to TA', style: 'danger', effect: 'reject' },
          ],
          createdAt: now(),
          isNew: true,
        })
        notify(routing.approverRole, { icon: '💰', title: `Offer approval — ${offer.candidateName}`, body: routing.label, link: '/approvals' })
        audit(actor, `Offer/${offer.id}`, 'SUBMITTED', { after: 'PendingApproval' })
        return offer
      }),

    approveOffer: (actor, offerId) =>
      run(() => {
        const offer = findOffer(offerId)
        approveOfferInternal(actor, offer, actor.role === 'mdceo' ? 'MD/CEO' : 'HR Head')
        return offer
      }),

    sendOffer: (actor, offerId) =>
      run(() => {
        requireCapability(actor, can(actor.role, 'offer.release'), 'Release requires HR authorisation.')
        const offer = findOffer(offerId)
        if (offer.state !== 'Approved') throw new ApiError(409, 'Offer must be approved before release')
        const bgv = db.bgvCases.find((b) => b.appId === offer.appId)
        if (!bgv) throw new ApiError(409, 'Cannot release — background verification has not been initiated')
        const s = summariseBgv(bgv)
        if (!s.releaseAllowed) throw new ApiError(409, `Cannot release — BGV checks not cleared: ${s.blocking.join(', ')}`)
        offer.state = 'Sent'
        offer.trail.unshift({ title: 'Offer letter released with e-sign request', detail: `${actor.name} · just now`, approved: true })
        audit(actor, `Offer/${offer.id}`, 'SENT', { before: 'Approved', after: 'Sent' })
        email('candidate', {
          tag: 'Offer',
          from: `${db.tenant.name} — Talent Acquisition <careers@${db.tenant.emailDomain}>`,
          to: 'candidate@email.example',
          subject: `🎓 Offer Letter — ${offer.designation} | ${db.tenant.name}`,
          paragraphs: [
            `Dear ${offer.candidateName},`,
            `We are pleased to offer you the position of ${offer.designation} (Grade ${offer.grade}), ${offer.location}, reporting to ${offer.reportingManager}.`,
            `Joining date: ${offer.joiningDate} · Probation: ${offer.probationMonths} months. Compensation is detailed in the attached letter.`,
            'Kindly e-sign within 5 working days.',
          ],
          cta: 'View & e-Sign Offer',
          signature: `Talent Acquisition Team · ${db.tenant.name}`,
        })
        return offer
      }),

    setOfferOutcome: (actor, offerId, outcome) =>
      run(() => {
        requireCapability(actor, can(actor.role, 'offer.edit'), 'Only HR-TA records the candidate response.')
        const offer = findOffer(offerId)
        if (offer.state !== 'Sent') throw new ApiError(409, 'Only a sent offer can be accepted or declined')
        offer.state = outcome
        offer.trail.unshift({ title: `Candidate ${outcome.toLowerCase()} the offer`, detail: `${actor.name} · just now`, approved: outcome === 'Accepted' })
        audit(actor, `Offer/${offer.id}`, outcome.toUpperCase(), { before: 'Sent', after: outcome })
        const app = db.applications.find((a) => a.appId === offer.appId)
        if (app && outcome === 'Accepted' && app.stage === 'Offer') moveStage(actor, app, 'PreBoarding')
        if (outcome === 'Declined') {
          // RCU-OFF-006: a decline is governed by HR Head.
          db.approvals.unshift({
            id: nextId('apr'),
            assigneeRole: 'hrhead',
            kind: 'Offer',
            tone: 'task',
            title: `Offer declined — ${offer.candidateName}`,
            chip: { text: 'Decline review', tone: 'slate' },
            meta: `${offer.designation} · ${offer.reqId} · notify consultant/referrer if applicable`,
            route: 'HR Head acknowledgement required before the requisition reopens sourcing',
            entity: { type: 'Offer', id: offer.id },
            actions: [{ id: 'ack', label: 'Acknowledge & reopen sourcing', style: 'primary', effect: 'resolve', resultText: 'Decline acknowledged' }],
            createdAt: now(),
            isNew: true,
          })
          notify('hrhead', { icon: '✉', title: `Offer declined — ${offer.candidateName}`, body: 'Acknowledgement needed.', link: '/approvals' })
        }
        return offer
      }),

    listApprovals: (role) => respond(db.approvals.filter((a) => a.assigneeRole === role)),

    decideApproval: (actor, id, actionId, reason) =>
      run(() => {
        const item = db.approvals.find((a) => a.id === id)
        if (!item) throw new ApiError(404, 'Approval item not found')
        requireCapability(actor, item.assigneeRole === actor.role, 'This item is not in your queue.')
        if (item.decision) throw new ApiError(409, 'Already decided')
        const action = item.actions.find((a) => a.id === actionId)
        if (!action) throw new ApiError(400, 'Unknown action')
        if (action.effect === 'query') {
          // RCU-APR-004: a query pauses the SLA clock and notifies the initiator.
          audit(actor, `Approval/${item.id}`, 'QUERY_SENT', { reason: action.label })
          notify('hrta', { icon: '❓', title: `Query on ${item.title}`, body: `${actor.name} sent a query · SLA clock paused.`, link: '/approvals' })
          return item
        }
        if (action.effect === 'reject' && !reason?.trim()) {
          throw new ApiError(400, 'A documented reason is mandatory')
        }
        // Apply the downstream effect first so a failed effect leaves the item undecided.
        applyApprovalEffect(actor, item, action.effect === 'reject', reason?.trim())
        const text = action.effect === 'reject' ? `${action.label} — reason recorded` : (action.resultText ?? action.label)
        item.decision = { text, by: actor.name, at: now(), reason: reason?.trim() || undefined }
        item.isNew = false
        audit(actor, `Approval/${item.id}`, action.effect === 'reject' ? 'REJECTED' : 'APPROVED', {
          reason: reason?.trim(),
          after: action.label,
        })
        return item
      }),

    listNotifications: (role) => respond(db.notifications.filter((n) => n.recipientRole === role)),
    listEmails: (role) => respond(db.emails.filter((e) => e.recipientRole === role)),
    markNotificationRead: (id) =>
      run(() => {
        const n = db.notifications.find((x) => x.id === id)
        if (n) n.unread = false
      }),
    markEmailRead: (id) =>
      run(() => {
        const e = db.emails.find((x) => x.id === id)
        if (e) e.unread = false
      }),
    markAllRead: (role) =>
      run(() => {
        db.notifications.filter((n) => n.recipientRole === role).forEach((n) => (n.unread = false))
        db.emails.filter((e) => e.recipientRole === role).forEach((e) => (e.unread = false))
      }),
    simulateEvent: (role) => run(() => simulate(role)),

    listJobPostings: () => respond(db.jobPostings),

    submitPublicApplication: (input) =>
      run(() => {
        // RCU-CAR-002: both consents are mandatory, enforced server-side.
        if (!input.privacyConsent || !input.coiDeclaration) {
          throw new ApiError(400, 'Data-privacy consent and conflict-of-interest declaration are mandatory')
        }
        if (!input.name.trim() || !input.email.trim() || !input.phone.trim()) {
          throw new ApiError(400, 'Name, email and mobile are required')
        }
        if (!input.resumeFileName) throw new ApiError(400, 'Please attach your resume')
        const posting = db.jobPostings.find((p) => p.id === input.postingId)
        if (!posting) throw new ApiError(400, 'Select a position to apply for')
        const at = now()
        const existing = db.candidates.find((c) => c.email.toLowerCase() === input.email.trim().toLowerCase())
        const candidate: Candidate = existing ?? {
          id: nextId('cand'),
          name: input.name.trim(),
          email: input.email.trim(),
          phone: input.phone.trim(),
          experienceYears: input.experienceYears ?? 0,
          summary: `${input.experienceYears ?? 0} yrs · career portal`,
          currentCtc: input.currentCtc,
          expectedCtc: input.expectedCtc,
          noticeDays: input.noticeDays,
          source: 'Portal',
          consents: [],
        }
        candidate.consents.push(
          { type: 'DataPrivacy', textVersion: 'v1', at },
          { type: 'ConflictOfInterest', textVersion: 'v1', at },
        )
        if (!existing) db.candidates.push(candidate)
        const application: Application = {
          appId: nextNumber('APP', db.applications.map((a) => a.appId)),
          reqId: posting.reqId,
          candidateId: candidate.id,
          stage: 'Sourced',
          stageHistory: [{ from: null, to: 'Sourced', by: 'Career portal', at }],
          note: existing ? 'Applied via career portal · possible duplicate — review' : 'Applied via career portal · consents captured',
        }
        db.applications.push(application)
        audit({ name: candidate.name, role: 'candidate' }, `Application/${application.appId}`, 'CREATED', { after: 'Sourced' })
        email('candidate', {
          tag: 'Confirmation',
          from: `${db.tenant.name} — Careers <careers@${db.tenant.emailDomain}>`,
          to: candidate.email,
          subject: `Application received — ${posting.title} [${application.appId}]`,
          paragraphs: [
            `Dear ${candidate.name},`,
            `Thank you for applying to ${db.tenant.name}. Application reference: ${application.appId} · ${posting.title}.`,
            'HR — Talent Acquisition reviews your profile within ~7 working days. Track your status anytime with your Application ID.',
            'Unsuccessful candidatures are retained for 1 year for audit and grievance redressal, then purged.',
          ],
          cta: 'Track Application Status',
          signature: `Talent Acquisition Team · ${db.tenant.name}`,
        })
        notify('candidate', { icon: '✅', title: `Application received — ${posting.title}`, body: `${application.appId} · under HR review ≤7 days.`, link: '/careers' })
        notify('hrta', {
          icon: '📥',
          title: `New application — ${posting.title}`,
          body: `${candidate.name} applied via career portal (${application.appId}) · consents captured.`,
          link: '/pipeline',
        })
        email('hrta', {
          tag: 'Sourcing',
          to: `ta@${db.tenant.emailDomain}`,
          subject: `📥 New application: ${posting.title} (${application.appId})`,
          paragraphs: [
            `A new application was received via the career portal and logged to ${posting.reqId}.`,
            `Candidate: ${candidate.name}. Consents: data privacy ✓ · COI declaration ✓.`,
            'Screen against the JD minimum criteria within the sourcing TAT.',
          ],
          signature: systemSignature(),
        })
        return { appId: application.appId, position: posting.title, duplicateOf: existing?.id }
      }),

    getApplicationStatus: (appId) => {
      const app = db.applications.find((a) => a.appId === appId.trim().toUpperCase())
      return respond(app ? coarseStatus(app.stage) : null)
    },

    listAudit: () => respond(db.audit),
  }

  function findOffer(id: string): Offer {
    const offer = db.offers.find((o) => o.id === id)
    if (!offer) throw new ApiError(404, 'Offer not found')
    return offer
  }

  function approveOfferInternal(actor: Actor, offer: Offer, byLabel: string) {
    const routing = offerRouting(db.rules, offer.grade, offer.components, offer.band)
    requireCapability(actor, actor.role === routing.approverRole, `Awaiting ${routing.label.split('→ ')[1] ?? 'the resolved approver'}.`)
    if (offer.state !== 'PendingApproval') throw new ApiError(409, `Offer is ${offer.state}`)
    offer.state = 'Approved'
    offer.trail.unshift({ title: `Approved by ${byLabel} at ₹${routing.total.toFixed(1)}L`, detail: 'Annexure D · immutable audit entry · just now', approved: true })
    audit(actor, `Offer/${offer.id}`, 'APPROVED', { before: 'PendingApproval', after: 'Approved' })
    // Approving from the offer screen or the inbox is the same single task (RCU-OFF-003).
    db.approvals
      .filter((a) => a.entity?.type === 'Offer' && a.entity.id === offer.id && !a.decision)
      .forEach((a) => (a.decision = { text: 'Offer approved (Annexure D)', by: actor.name, at: now() }))
    notify('hrta', { icon: '✅', title: `Offer approved — ${offer.candidateName}`, body: `${byLabel} approved ₹${routing.total.toFixed(1)}L. Release after BGV clearance.`, link: '/offer' })
  }

  function applyApprovalEffect(actor: Actor, item: ApprovalItem, rejected: boolean, reason?: string) {
    if (item.entity?.type === 'Requisition') {
      const req = db.requisitions.find((r) => r.reqId === item.entity?.id)
      if (req && req.state === 'PendingApproval') {
        setRequisitionState(actor, req, rejected ? 'Rejected' : 'Approved', reason)
        // RCU-APR-006: the initiator hears about every decision.
        notify('hrta', {
          icon: rejected ? '✖' : '✅',
          title: `MRF ${req.reqId} ${rejected ? 'rejected' : 'approved'}`,
          body: rejected ? `Reason: ${reason}` : 'Sourcing is unlocked for this requisition.',
          link: '/pipeline',
        })
        email('hrta', {
          tag: 'Decision',
          to: roleMailbox('hrta'),
          subject: `MRF ${req.reqId} ${rejected ? 'rejected' : 'approved'} by ${actor.name}`,
          paragraphs: [
            `Decision: ${rejected ? 'Rejected' : 'Approved'} · ${actor.name} (${actor.role}) · ${new Date().toLocaleString()}`,
            ...(rejected && reason ? [`Reason: ${reason}`] : []),
          ],
          signature: systemSignature(),
        })
      }
    }
    if (item.entity?.type === 'Offer') {
      const offer = db.offers.find((o) => o.id === item.entity?.id)
      if (offer && offer.state === 'PendingApproval') {
        if (rejected) {
          offer.state = 'Draft'
          offer.trail.unshift({ title: 'Returned for CTC rework', detail: `${actor.name} · reason: ${reason}` })
          audit(actor, `Offer/${offer.id}`, 'RETURNED', { before: 'PendingApproval', after: 'Draft', reason })
        } else {
          approveOfferInternal(actor, offer, actor.role === 'mdceo' ? 'MD/CEO (via Approvals inbox)' : 'HR Head (via Approvals inbox)')
        }
      }
    }
  }

  function coarseStatus(stage: ApplicationStage): string {
    // RCU-CAR-003: coarse-grained, no sensitive detail.
    switch (stage) {
      case 'Sourced':
        return 'Under HR review — screening call within ≤7 days'
      case 'Screened':
      case 'Interview':
      case 'Selection':
        return 'Interviews in progress'
      case 'BGV':
      case 'Offer':
      case 'PreBoarding':
        return 'Final stages — our team will be in touch'
      case 'Onboarded':
      case 'Confirmed':
        return 'Welcome aboard'
      case 'Hold':
        return 'Under review'
      case 'Rejected':
      case 'Withdrawn':
        return 'Closed — thank you for your interest'
    }
  }

  function simulate(role: Role): string {
    const t = db.tenant
    switch (role) {
      case 'hrta':
        notify('hrta', { icon: '📥', title: 'New application — Credit Analyst, Jaipur', body: 'A. Khan applied via career portal — auto-logged to pipeline.', link: '/pipeline' })
        email('hrta', {
          tag: 'Sourcing',
          to: `ta@${t.emailDomain}`,
          subject: '📥 New application: Credit Analyst — Jaipur',
          paragraphs: ['A new application has been received via the career portal and matched to REQ-2026-0153.', 'Screen against the JD minimum criteria within the sourcing TAT.'],
          signature: systemSignature(),
        })
        return 'New application routed to pipeline + inbox'
      case 'hrhead':
        notify('hrhead', { icon: '⏱', title: 'SLA reminder — MRF approval due in 4h', body: 'REQ-2026-0150 · 2-day SLA ending today.', link: '/approvals' })
        return 'SLA reminder pushed'
      case 'mdceo':
        notify('mdceo', { icon: '📈', title: 'Quarterly KPI pack ready', body: 'Q2 summary — offer-to-join 85%, diversity 31%.', link: '/' })
        return 'Quarterly pack notification pushed'
      case 'employee':
        notify('employee', { icon: '📅', title: 'IJP status: interview scheduled', body: 'Deputy Manager — Credit Ops · internal panel, Fri 11:00.', link: '/internal-careers' })
        return 'Interview invite pushed'
      case 'candidate':
        notify('candidate', { icon: '📅', title: 'Screening invite — HR round', body: 'APP-2026-0417 · telephonic · slot options emailed.', link: '/careers' })
        email('candidate', {
          tag: 'Interview',
          from: `${t.name} — Careers <careers@${t.emailDomain}>`,
          to: 'candidate@email.example',
          subject: '📅 Screening invite — HR round (telephonic) | APP-2026-0417',
          paragraphs: ['Congratulations — your profile has been shortlisted for the first screening round.', 'Slot options: Tomorrow 11:00 / Tomorrow 16:00 / Day after 10:30.'],
          signature: `Talent Acquisition Team · ${t.name}`,
        })
        return 'Screening invite pushed — check the email log'
    }
  }
}

