// RBAC matrix from FRD §3.2. The UI uses it to hide or disable controls; the .NET services
// must enforce the same matrix server-side (RCU-PLT-002). Never treat this as the security boundary.
import { useCallback } from 'react'
import type { Role, WorkspaceRoleConfig } from '../domain/types'
import { useSession } from './sessionContext'
import { useWorkspaceRoles } from './workspaceRoles'

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

/**
 * Whether `role` may use `capability`. When the workspace configured that role in the admin portal, its
 * configured permissions decide; otherwise (roles the workspace doesn't define, such as candidate, or no
 * configuration loaded) the FRD matrix above does.
 */
export function can(role: Role, capability: Capability, workspaceRoles?: WorkspaceRoleConfig[] | null): boolean {
  const configured = workspaceRoles?.find((r) => r.key === role)
  return configured ? configured.permissions.includes(capability) : matrix[capability].includes(role)
}

/**
 * Whether `role` may open the screen `screen` (admin screen catalogue key). Null when the workspace hasn't
 * configured the role, so the caller applies its own default.
 */
export function canOpen(role: Role, screen: string, workspaceRoles?: WorkspaceRoleConfig[] | null): boolean | null {
  const configured = workspaceRoles?.find((r) => r.key === role)
  return configured ? configured.pages.includes(screen) : null
}

/** `can` for the signed-in user in their workspace. */
export function useCan(): (capability: Capability) => boolean {
  const { user } = useSession()
  const roles = useWorkspaceRoles()
  return useCallback((capability: Capability) => can(user.role, capability, roles), [user.role, roles])
}

/** `canOpen` for the signed-in user in their workspace. */
export function useCanOpen(): (screen: string) => boolean | null {
  const { user } = useSession()
  const roles = useWorkspaceRoles()
  return useCallback((screen: string) => canOpen(user.role, screen, roles), [user.role, roles])
}

/** Where each persona lands after signing in or switching persona. */
export function homePath(role: Role): string {
  if (role === 'candidate') return '/careers'
  if (role === 'employee') return '/internal-careers'
  return '/'
}
