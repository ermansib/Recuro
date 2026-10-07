// Pure rule functions. They take the versioned RuleConfig as input (RCU-PLT-005) so policy
// changes are data changes, and so the same logic can be ported to the .NET rules engine.
import { addWorkingDays, toIsoDate } from '../utils/workingDays'
import type { BgvCase, BgvCheckStatus, DoaRoute, Grade, InterviewRound, Role, RuleConfig } from './types'

export function resolveDoa(config: RuleConfig, grade: Grade): DoaRoute {
  const route = config.doa.find((r) => r.grade === grade)
  if (!route) throw new Error(`No DOA route configured for grade ${grade}`)
  return route
}

/** RCU-MRF-004: target closure = today + the overall TAT upper bound for the level. */
export function targetClosureDate(route: DoaRoute, from: Date): string {
  return toIsoDate(addWorkingDays(from, route.overallTat.maxDays))
}

export interface OfferRouting {
  total: number
  deviation: number
  withinBand: boolean
  label: string
  approverRole: Role
}

/** RCU-OFF-002/003: band check and Annexure D routing. */
export function offerRouting(
  config: RuleConfig,
  grade: Grade,
  components: { fixed: number; variable: number; benefits: number },
  band: { min: number; max: number },
): OfferRouting {
  const total = round1(components.fixed + components.variable + components.benefits)
  const deviation = round1(total - band.max)
  const rule = config.offerMatrix.find((r) => r.levels.includes(grade))
  if (!rule) throw new Error(`No offer approval rule for grade ${grade}`)
  const withinBand = deviation <= 0
  const leg = withinBand ? rule.withinBand : rule.deviation
  return { total, deviation, withinBand, label: leg.label, approverRole: leg.approverRole }
}

export interface BgvSummary {
  cleared: number
  inProgress: number
  pending: number
  flagged: number
  /** RCU-BGV-005: offer release is allowed only when every applicable check is cleared. */
  releaseAllowed: boolean
  blocking: string[]
}

export function summariseBgv(bgv: BgvCase): BgvSummary {
  const count = (s: BgvCheckStatus) => bgv.checks.filter((c) => c.status === s).length
  const blocking = bgv.checks
    .filter((c) => c.status !== 'Cleared' && c.status !== 'NotApplicable')
    .map((c) => c.label)
  return {
    cleared: count('Cleared'),
    inProgress: count('InProgress'),
    pending: count('Pending'),
    flagged: count('Flagged'),
    releaseAllowed: blocking.length === 0 && bgv.consentAt !== null,
    blocking,
  }
}

/** RCU-ASM-003: N/A rows are excluded from the average. Returns null when nothing is rated. */
export function assessmentAverage(ratings: InterviewRound['ratings']): number | null {
  const scored = ratings.filter((r) => !r.na && r.score !== null)
  if (scored.length === 0) return null
  const sum = scored.reduce((acc, r) => acc + (r.score ?? 0), 0)
  return round1(sum / scored.length)
}

export const RECOMMEND_THRESHOLD = 3.5

function round1(n: number): number {
  return Math.round(n * 10) / 10
}
