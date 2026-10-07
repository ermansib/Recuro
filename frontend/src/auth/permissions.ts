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
