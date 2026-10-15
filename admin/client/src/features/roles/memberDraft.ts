import { isValidEmail, unmetPasswordRules } from '@workspace-form'
import type { CreateMemberRequest } from '../../api/types'

/** Same limits as WorkspaceMember in the admin API. */
export const MEMBER_LIMITS = { name: 120, department: 80 } as const


export interface MemberDraft {
  name: string
  email: string
  roleKeys: string[]
  department: string
  temporaryPassword: string
}

export const EMPTY_MEMBER: MemberDraft = { name: '', email: '', roleKeys: [], department: '', temporaryPassword: '' }

export type MemberField = 'name' | 'email' | 'roleKeys' | 'temporaryPassword'

/** Problems with the draft, as `roles.members.errors.<field>` keys. Empty means it can be sent. */
export function validateMember(draft: MemberDraft): Partial<Record<MemberField, string>> {
  const errors: Partial<Record<MemberField, string>> = {}
  const name = draft.name.trim()
  if (!name || name.length > MEMBER_LIMITS.name) errors.name = 'roles.members.errors.name'
  if (!isValidEmail(draft.email)) errors.email = 'roles.members.errors.email'
  if (draft.roleKeys.length === 0) errors.roleKeys = 'roles.members.errors.roles'
  if (unmetPasswordRules(draft.temporaryPassword).length > 0) errors.temporaryPassword = 'roles.members.errors.password'
  return errors
}

export function toCreateRequest(draft: MemberDraft): CreateMemberRequest {
  return {
    name: draft.name.trim(),
    email: draft.email.trim(),
    roleKeys: draft.roleKeys,
    department: draft.department.trim() || null,
    temporaryPassword: draft.temporaryPassword,
  }
}
