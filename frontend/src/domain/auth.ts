// Pure identity rules shared by the UI and the mock. The .NET identity service must apply the
// same policy (RCU-PLT-001, NFR-01), so keep these free of React and storage.
import type { OrgType, Role, SsoProvider, StaffRole, TenantConfig } from './types'

export const PASSWORD_MIN_LENGTH = 8
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

export type PasswordRule = 'length' | 'upper' | 'lower' | 'digit' | 'symbol'

const PASSWORD_RULES: Record<PasswordRule, (pw: string) => boolean> = {
  length: (pw) => pw.length >= PASSWORD_MIN_LENGTH,
  upper: (pw) => /[A-Z]/.test(pw),
  lower: (pw) => /[a-z]/.test(pw),
  digit: (pw) => /\d/.test(pw),
  symbol: (pw) => /[^A-Za-z0-9]/.test(pw),
}

export const PASSWORD_RULE_ORDER = Object.keys(PASSWORD_RULES) as PasswordRule[]

/** Rules the password still fails, in display order. Empty means it is acceptable. */
export function unmetPasswordRules(password: string): PasswordRule[] {
  return PASSWORD_RULE_ORDER.filter((rule) => !PASSWORD_RULES[rule](password))
}

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

export function isValidEmail(email: string): boolean {
  return EMAIL_PATTERN.test(email.trim())
}

export function normaliseEmail(email: string): string {
  return email.trim().toLowerCase()
}

/** `Aurora Housing Finance` → `aurora-housing-finance`. */
export function slugify(name: string): string {
  return name
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '') // drop accents: Ü → U
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 40)
}

/** `k.iyer@aurora.example` → `k.***@aurora.example`, so screens confirm a destination without exposing it. */
export function maskEmail(email: string): string {
  const [local = '', domain = ''] = email.split('@')
  const visible = local.slice(0, Math.min(2, Math.max(local.length - 1, 1)))
  return `${visible}***@${domain}`
}

export interface OrgTypeDefaults {
  adminRole: Exclude<StaffRole, 'employee'>
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
    adminRole: 'hrta',
    ssoProviders: ['google', 'microsoft'],
    mfaRoles: ['hrhead', 'mdceo'],
    careersTagline: 'Build something great with us',
  },
  agency: {
    adminRole: 'hrhead',
    ssoProviders: ['google', 'microsoft'],
    mfaRoles: ['hrhead', 'mdceo'],
    careersTagline: 'Find your next role through us',
  },
  enterprise: {
    adminRole: 'hrhead',
    ssoProviders: ['microsoft', 'google', 'saml'],
    mfaRoles: ['hrhead', 'mdceo'],
    careersTagline: 'Grow your career where it matters',
  },
}

export function requiresMfa(tenant: Pick<TenantConfig, 'mfaRoles'>, role: Role): boolean {
  return tenant.mfaRoles.includes(role)
}
