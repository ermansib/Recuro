// Pure identity rules shared by the UI and the mock. The .NET identity service must apply the
// same policy (RCU-PLT-001, NFR-01), so keep these free of React and storage.
import { WORKSPACE_FORM, workspaceSlug, type WorkspaceOwnerRole } from '@workspace-form'
import type { OrgType, Role, SsoProvider, StaffRole, TenantConfig } from './types'

export const PASSWORD_MIN_LENGTH = WORKSPACE_FORM.passwordMinLength
/** RCU-PLT-001: the account locks after this many failed attempts in a row. */
export const MAX_FAILED_SIGN_INS = 5
export const LOCKOUT_MINUTES = 15
/** NFR-01 default; a tenant can change it in its config. */
export const DEFAULT_IDLE_MINUTES = 30
export const INVITE_VALID_DAYS = 7
export const RESET_LINK_VALID_MINUTES = 30
export const MFA_CODE_LENGTH = 6

/** Staff roles an administrator can invite, in menu order. Candidates sign up on the careers site. */
export const INVITABLE_ROLES: StaffRole[] = ['hrta', 'hrhead', 'mdceo', 'employee']

export type { PasswordRule } from '@workspace-form'
export { PASSWORD_RULE_ORDER, isValidEmail, unmetPasswordRules } from '@workspace-form'

export function normaliseEmail(email: string): string {
  return email.trim().toLowerCase()
}

/** Up to two initials for an avatar, e.g. `Asha Rao` → `AR`. */
export function initialsOf(name: string): string {
  return (
    name
      .split(/[\s.]+/)
      .filter(Boolean)
      .map((p) => p[0]?.toUpperCase() ?? '')
      .join('')
      .slice(0, 2) || '?'
  )
}

/** `Aurora Housing Finance` → `aurora-housing-finance`; the same rule as the admin API's workspace slugs. */
export const slugify = workspaceSlug

/** `k.iyer@aurora.example` → `k.***@aurora.example`, so screens confirm a destination without exposing it. */
export function maskEmail(email: string): string {
  const [local = '', domain = ''] = email.split('@')
  const visible = local.slice(0, Math.min(2, Math.max(local.length - 1, 1)))
  return `${visible}***@${domain}`
}

export interface OrgTypeDefaults {
  adminRole: WorkspaceOwnerRole
  ssoProviders: SsoProvider[]
  mfaRoles: Role[]
  careersTagline: string
}

/**
 * Starting configuration for a new workspace. Org type only changes these defaults: small
 * businesses, agencies and enterprises all get the same features and can change any of this later.
 */
export const ORG_TYPE_DEFAULTS: Record<OrgType, OrgTypeDefaults> = {
  smallBusiness: {
    adminRole: WORKSPACE_FORM.defaultOwnerRole.smallBusiness,
    ssoProviders: ['google', 'microsoft'],
    mfaRoles: ['hrhead', 'mdceo'],
    careersTagline: 'Build something great with us',
  },
  agency: {
    adminRole: WORKSPACE_FORM.defaultOwnerRole.agency,
    ssoProviders: ['google', 'microsoft'],
    mfaRoles: ['hrhead', 'mdceo'],
    careersTagline: 'Find your next role through us',
  },
  enterprise: {
    adminRole: WORKSPACE_FORM.defaultOwnerRole.enterprise,
    ssoProviders: ['microsoft', 'google', 'saml'],
    mfaRoles: ['hrhead', 'mdceo'],
    careersTagline: 'Grow your career where it matters',
  },
}

export function requiresMfa(tenant: Pick<TenantConfig, 'mfaRoles'>, role: Role): boolean {
  return tenant.mfaRoles.includes(role)
}
