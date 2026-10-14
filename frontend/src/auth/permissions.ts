// RBAC matrix from FRD §3.2. The UI uses it to hide or disable controls; the .NET services
// must enforce the same matrix server-side (RCU-PLT-002). Never treat this as the security boundary.
import type { Role } from '../domain/types'

export type Capability =
  | 'mrf.raise'
  | 'mrf.approve'
  | 'candidate.viewSensitive'
  | 'candidate.log'
  | 'pipeline.move'
  | 'assessment.submit'
  | 'bgv.initiate'
  | 'bgv.reportAdverse'
  | 'offer.edit'
  | 'offer.release'
  | 'jd.edit'
  | 'approvals.view'
  | 'internal.view'
  | 'careers.apply'
  | 'team.invite'

const matrix: Record<Capability, readonly Role[]> = {
  'mrf.raise': ['hrta'],
  'mrf.approve': ['hrhead', 'mdceo'],
  'candidate.viewSensitive': ['hrta', 'hrhead'],
  'candidate.log': ['hrta'],
  'pipeline.move': ['hrta', 'hrhead'],
  // HR-TA acts as the assigned interviewer's admin in this phase (RCU-ASM-002).
  'assessment.submit': ['hrta'],
  'bgv.initiate': ['hrta'],
  'bgv.reportAdverse': ['hrta'],
  'offer.edit': ['hrta'],
  'offer.release': ['hrta', 'hrhead'],
  'jd.edit': ['hrta'],
  'approvals.view': ['hrta', 'hrhead', 'mdceo'],
  'internal.view': ['hrta', 'hrhead', 'mdceo'],
  'careers.apply': ['candidate'],
  // Workspace administration: HR roles invite colleagues; candidates self-register (RCU-PLT-001).
  'team.invite': ['hrta', 'hrhead'],
}

export function can(role: Role, capability: Capability): boolean {
  return matrix[capability].includes(role)
}

/** Where each persona lands after signing in or switching persona. */
export function homePath(role: Role): string {
  if (role === 'candidate') return '/careers'
  if (role === 'employee') return '/internal-careers'
  return '/'
}

/** Roles the portal has screens for, in the order one is chosen when a person holds several. */
const PERSONA_ORDER: readonly Role[] = ['hrhead', 'mdceo', 'hrta', 'employee', 'candidate']

/** Permissions that only the talent-acquisition screens use. */
const TA_WORK = ['mrf.raise', 'candidate.log', 'jd.edit', 'offer.edit', 'bgv.initiate', 'assessment.submit']
const APPROVER_WORK = ['approvals.view', 'mrf.approve', 'offer.approve', 'bgv.decideAdverse', 'reports.view', 'internal.view', 'pipeline.move', 'offer.release']
const EMPLOYEE_WORK = ['ijp.apply', 'referral.submit']

/**
 * Which portal persona a person sees, from the role keys their workspace gave them and those roles' permissions.
 * A persona key they hold wins; a role the workspace created itself (or HOD) gets the persona its permissions
 * fit, and an approver without `candidate.viewSensitive` gets the masked MD/CEO view (RCU-PLT-002). `null` means
 * nothing the portal can show. Like the matrix above, this only shapes the UI.
 */
export function personaFor(roleKeys: readonly string[], permissions: readonly string[]): Role | null {
  const held = PERSONA_ORDER.find((r) => roleKeys.includes(r))
  if (held) return held
  const has = (keys: readonly string[]) => keys.some((k) => permissions.includes(k))
  if (has(TA_WORK)) return 'hrta'
  if (has(APPROVER_WORK)) return permissions.includes('candidate.viewSensitive') ? 'hrhead' : 'mdceo'
  if (has(EMPLOYEE_WORK)) return 'employee'
  return null
}
