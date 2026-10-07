import rulesJson from '../mocks/data/rules.json'
import { assessmentAverage, offerRouting, resolveDoa, summariseBgv, targetClosureDate } from './rules'
import { applicationTransitions, canTransition } from './stateMachines'
import type { BgvCase, RuleConfig } from './types'

const rules = rulesJson as RuleConfig

describe('DOA routing (RCU-MRF-002)', () => {
  it('routes M3 to the HR Head and VP to the MD/CEO', () => {
    expect(resolveDoa(rules, 'M3')).toMatchObject({ initiating: 'HOD', recommending: 'Function Head', approving: 'HR Head', approverRole: 'hrhead' })
    expect(resolveDoa(rules, 'VP').approverRole).toBe('mdceo')
    expect(resolveDoa(rules, 'KMP').approving).toBe('Board / NRC')
  })

  it('sets target closure to the overall TAT upper bound in working days (RCU-MRF-004)', () => {
    // Monday 3 Aug 2026 + 20 working days (E grade) = Monday 31 Aug 2026.
    expect(targetClosureDate(resolveDoa(rules, 'E'), new Date(2026, 7, 3))).toBe('2026-08-31')
  })
})

describe('offer routing (RCU-OFF-002, Annexure D)', () => {
  const band = { min: 18, max: 24 }
  it('sends within-band M3 offers to the HR Head', () => {
    const r = offerRouting(rules, 'M3', { fixed: 17, variable: 2.8, benefits: 1.7 }, band)
    expect(r).toMatchObject({ total: 21.5, withinBand: true, approverRole: 'hrhead' })
  })
  it('flips deviations above the band cap to the MD/CEO', () => {
    const r = offerRouting(rules, 'M3', { fixed: 19.6, variable: 3.2, benefits: 2 }, band)
    expect(r).toMatchObject({ total: 24.8, deviation: 0.8, withinBand: false, approverRole: 'mdceo' })
  })
  it('treats exactly the cap as within band', () => {
    expect(offerRouting(rules, 'M3', { fixed: 20, variable: 3, benefits: 1 }, band).withinBand).toBe(true)
  })
})

describe('BGV release gate (RCU-BGV-005)', () => {
  const base: BgvCase = {
    id: 'b', appId: 'a', vendor: 'v', vendorCaseRef: 'r', consentAt: '2026-08-01', initiatedAt: '2026-08-01', tatDay: 1, tatTotal: 15,
    checks: [
      { type: 'identity', label: 'Identity', detail: '', status: 'Cleared', note: '', date: null },
      { type: 'fitProper', label: 'Fit & Proper', detail: '', status: 'NotApplicable', note: '', date: null },
    ],
  }
  it('allows release when every applicable check is cleared', () => {
    expect(summariseBgv(base).releaseAllowed).toBe(true)
  })
  it('blocks release and lists pending or flagged checks', () => {
    const c: BgvCase = { ...base, checks: [...base.checks, { type: 'police', label: 'Police', detail: '', status: 'Flagged', note: '', date: null }] }
    expect(summariseBgv(c)).toMatchObject({ releaseAllowed: false, flagged: 1, blocking: ['Police'] })
  })
  it('blocks release without consent (RCU-BGV-001)', () => {
    expect(summariseBgv({ ...base, consentAt: null }).releaseAllowed).toBe(false)
  })
})

describe('assessment average (RCU-ASM-003)', () => {
  it('excludes N/A rows', () => {
    const avg = assessmentAverage([
      { competencyId: 'a', score: 4, na: false, comment: '' },
      { competencyId: 'b', score: 1, na: true, comment: '' },
      { competencyId: 'c', score: 5, na: false, comment: '' },
    ])
    expect(avg).toBe(4.5)
  })
  it('returns null when nothing is rated', () => {
    expect(assessmentAverage([{ competencyId: 'a', score: null, na: false, comment: '' }])).toBeNull()
  })
})

describe('application state machine (FRD §6.2)', () => {
  it('allows forward moves and side exits only', () => {
    expect(canTransition(applicationTransitions, 'Sourced', 'Screened')).toBe(true)
    expect(canTransition(applicationTransitions, 'Sourced', 'Offer')).toBe(false)
    expect(canTransition(applicationTransitions, 'Interview', 'Rejected')).toBe(true)
    expect(canTransition(applicationTransitions, 'Rejected', 'Sourced')).toBe(false)
  })
})
