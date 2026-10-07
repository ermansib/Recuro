import type { RequisitionInput } from '../../domain/types'
import { ApiError, type Actor } from '../contract'
import { createMockClient } from './mockClient'

const hrta: Actor = { name: 'A. Sharma', role: 'hrta' }
const hrhead: Actor = { name: 'K. Iyer', role: 'hrhead' }
const mdceo: Actor = { name: 'V. Raghavan', role: 'mdceo' }

const mrf = (over: Partial<RequisitionInput> = {}): RequisitionInput => ({
  department: 'Credit & Risk',
  designation: 'Credit Analyst',
  grade: 'M3',
  location: 'HQ — Mumbai',
  positions: 1,
  reportingManager: 'N. Sharma — DVP, Credit',
  employmentType: 'Permanent',
  nature: 'NewPosition',
  replacementReason: '',
  joiningDate: '2026-10-01',
  band: '₹18L – ₹24L',
  outOfBudget: false,
  oobJustification: '',
  qualifications: 'MBA',
  sourcingChannels: [],
  ...over,
})

async function expectStatus(p: Promise<unknown>, status: number) {
  await expect(p).rejects.toBeInstanceOf(ApiError)
  await expect(p).rejects.toMatchObject({ status })
}

describe('MRF → approval → sourcing lock', () => {
  it('routes a new M3 MRF to the HR Head and unlocks sourcing on approval', async () => {
    const api = createMockClient()
    const req = await api.createRequisition(hrta, mrf())
    expect(req.reqId).toMatch(/^REQ-\d{4}-0157$/)
    expect(req.state).toBe('PendingApproval')

    // RCU-MRF-006: sourcing is locked while pending.
    await expectStatus(api.logCandidate(hrta, { reqId: req.reqId, name: 'X', email: 'x@e.x', phone: '1', experienceYears: 1, source: 'Portal', privacyConsent: true }), 409)

    const inbox = await api.listApprovals('hrhead')
    const item = inbox.find((a) => a.entity?.id === req.reqId)
    expect(item).toBeDefined()
    expect((await api.listNotifications('hrhead'))[0]?.title).toContain(req.reqId)

    await api.decideApproval(hrhead, item!.id, 'approve')
    const updated = (await api.listRequisitions()).find((r) => r.reqId === req.reqId)
    expect(updated?.state).toBe('Approved')
    expect((await api.listNotifications('hrta'))[0]?.title).toContain('approved')

    const card = await api.logCandidate(hrta, { reqId: req.reqId, name: 'X', email: 'x@e.x', phone: '1', experienceYears: 1, source: 'Portal', privacyConsent: true })
    expect(card.application.stage).toBe('Sourced')
  })

  it('sends out-of-budget MRFs to the MD/CEO and requires a justification (RCU-MRF-003)', async () => {
    const api = createMockClient()
    await expectStatus(api.createRequisition(hrta, mrf({ outOfBudget: true })), 400)
    const req = await api.createRequisition(hrta, mrf({ outOfBudget: true, oobJustification: 'Board audit' }))
    expect(req.route.approverRole).toBe('mdceo')
  })

  it('rejects only with a documented reason (RCU-APR-003)', async () => {
    const api = createMockClient()
    const [item] = (await api.listApprovals('hrhead')).filter((a) => a.kind === 'MRF')
    await expectStatus(api.decideApproval(hrhead, item!.id, 'reject', '  '), 400)
    const decided = await api.decideApproval(hrhead, item!.id, 'reject', 'Budget freeze')
    expect(decided.decision?.reason).toBe('Budget freeze')
  })

  it('forbids raising MRFs and deciding other queues (RCU-PLT-002)', async () => {
    const api = createMockClient()
    await expectStatus(api.createRequisition(hrhead, mrf()), 403)
    const [item] = await api.listApprovals('hrhead')
    await expectStatus(api.decideApproval(mdceo, item!.id, item!.actions[0]!.id), 403)
    expect((await api.listAudit()).some((e) => e.action === 'ACCESS_DENIED')).toBe(true)
  })
})

describe('pipeline', () => {
  it('validates moves against the state machine and keeps MD/CEO read-only', async () => {
    const api = createMockClient()
    await expectStatus(api.moveApplication(hrta, 'APP-2026-0380', 'Offer'), 409)
    await expectStatus(api.moveApplication(mdceo, 'APP-2026-0380', 'Screened'), 403)
    const moved = await api.moveApplication(hrta, 'APP-2026-0380', 'Screened')
    expect(moved.stage).toBe('Screened')
  })

  it('queues a regret email and 1-year retention on rejection (RCU-PIP-005)', async () => {
    const api = createMockClient()
    await expectStatus(api.rejectApplication(hrta, 'APP-2026-0380', ''), 400)
    const app = await api.rejectApplication(hrta, 'APP-2026-0380', 'Notice period too long')
    expect(app.rejection?.regretDueBy).toBeTruthy()
    expect((await api.listEmails('candidate'))[0]?.paragraphs.join(' ')).not.toContain('Notice period')
  })

  it('flags duplicates by email (RCU-PIP-002)', async () => {
    const api = createMockClient()
    await expectStatus(
      api.logCandidate(hrta, { reqId: 'REQ-2026-0156', name: 'R', email: 'RAHUL.MEHTA@email.example', phone: '', experienceYears: 1, source: 'Portal', privacyConsent: true }),
      409,
    )
  })
})

describe('offers and BGV', () => {
  it('only the resolved authority can approve a deviation, and the inbox card closes with it', async () => {
    const api = createMockClient()
    await expectStatus(api.approveOffer(hrhead, 'off-0388'), 403)
    const offer = await api.approveOffer(mdceo, 'off-0388')
    expect(offer.state).toBe('Approved')
    const card = (await api.listApprovals('mdceo')).find((a) => a.entity?.id === 'off-0388')
    expect(card?.decision).toBeDefined()
  })

  it('blocks release while BGV is incomplete (RCU-BGV-005)', async () => {
    const api = createMockClient()
    await api.approveOffer(mdceo, 'off-0388')
    await expectStatus(api.sendOffer(hrta, 'off-0388'), 409)
    await expectStatus(api.releaseOfferAfterBgv(hrta, 'bgv-0390'), 409)
  })

  it('escalates adverse findings to HR Head and MD/CEO (RCU-BGV-004)', async () => {
    const api = createMockClient()
    const bgv = await api.reportAdverseFinding(hrta, 'bgv-0390', { check: 'police', description: 'Address mismatch', action: 'HoldAndEscalate' })
    expect(bgv.checks.find((c) => c.type === 'police')?.status).toBe('Flagged')
    expect((await api.listApprovals('hrhead'))[0]?.kind).toBe('AdverseBgv')
    expect((await api.listApprovals('mdceo'))[0]?.kind).toBe('AdverseBgv')
  })

  it('locks submitted interview feedback (RCU-ASM-005)', async () => {
    const api = createMockClient()
    const round = await api.getInterview('APP-2026-0386')
    await api.submitInterview(hrta, round)
    await expectStatus(api.submitInterview(hrta, round), 409)
  })
})

describe('public career site', () => {
  const input = {
    postingId: 'post-0153', name: 'Neel', email: 'neel@e.x', phone: '99', experienceYears: 3, currentCtc: 8, expectedCtc: 10,
    noticeDays: 30, resumeFileName: 'cv.pdf', privacyConsent: true, coiDeclaration: true,
  }
  it('requires both consents (RCU-CAR-002)', async () => {
    const api = createMockClient()
    await expectStatus(api.submitPublicApplication({ ...input, coiDeclaration: false }), 400)
  })
  it('issues an application ID, creates the application and notifies HR-TA (RCU-CAR-003/004)', async () => {
    const api = createMockClient()
    const res = await api.submitPublicApplication(input)
    expect(res.appId).toMatch(/^APP-\d{4}-\d{4}$/)
    expect(await api.getApplicationStatus(res.appId.toLowerCase())).toContain('Under HR review')
    expect((await api.listPipeline('REQ-2026-0153')).some((c) => c.application.appId === res.appId)).toBe(true)
    expect((await api.listNotifications('hrta'))[0]?.title).toContain('New application')
  })
})
