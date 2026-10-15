import { isValidEmail, unmetPasswordRules } from '@workspace-form'
import type { CreateMemberRequest } from '../../api/types'

/** Same limits as WorkspaceMember in the admin API. */
export const MEMBER_LIMITS = { name: 120, department: 80 } as const

const SUGGESTED_LENGTH = 14
const CHARACTER_SETS = ['ABCDEFGHJKLMNPQRSTUVWXYZ', 'abcdefghijkmnpqrstuvwxyz', '23456789', '!@#$%&*?'] as const

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

function randomIndex(size: number): number {
  const value = new Uint32Array(1)
  crypto.getRandomValues(value)
  return value[0]! % size
}

function pick(characters: string): string {
  return characters[randomIndex(characters.length)]!
}

/** A random password that meets every password rule, without look-alike characters (0/O, 1/l/I). */
export function suggestTemporaryPassword(): string {
  const all = CHARACTER_SETS.join('')
  const characters = CHARACTER_SETS.map(pick)
  while (characters.length < SUGGESTED_LENGTH) characters.push(pick(all))
  for (let i = characters.length - 1; i > 0; i--) {
    const j = randomIndex(i + 1)
    ;[characters[i], characters[j]] = [characters[j]!, characters[i]!]
  }
  return characters.join('')
}
