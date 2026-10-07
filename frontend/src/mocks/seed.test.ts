// Seed invariants: the static JSON must stay consistent, because the future .NET services will
// be checked against the same fixtures.
import { loadSeed } from '../api/mock/seed'
import { canTransition, requisitionTransitions } from '../domain/stateMachines'

const db = loadSeed()

describe('seed data', () => {
  it('links every application to a real requisition and candidate', () => {
    const reqs = new Set(db.requisitions.map((r) => r.reqId))
    const cands = new Set(db.candidates.map((c) => c.id))
    for (const a of db.applications) {
      expect(reqs.has(a.reqId), a.appId).toBe(true)
      expect(cands.has(a.candidateId), a.appId).toBe(true)
    }
  })

  it('uses unique ids', () => {
    for (const ids of [db.requisitions.map((r) => r.reqId), db.applications.map((a) => a.appId), db.approvals.map((a) => a.id)]) {
      expect(new Set(ids).size).toBe(ids.length)
    }
  })

  it('only uses known requisition states', () => {
    for (const r of db.requisitions) expect(Object.keys(requisitionTransitions)).toContain(r.state)
    expect(canTransition(requisitionTransitions, 'PendingApproval', 'Approved')).toBe(true)
  })

  it('points offers, BGV cases and interviews at existing applications', () => {
    const apps = new Set(db.applications.map((a) => a.appId))
    for (const x of [...db.offers, ...db.bgvCases, ...db.interviews]) expect(apps.has(x.appId), x.id).toBe(true)
  })

  it('contains no real-looking email domains', () => {
    for (const c of db.candidates) expect(c.email).toMatch(/\.example$/)
  })
})
