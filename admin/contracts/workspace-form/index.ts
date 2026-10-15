// The one definition of the "create a workspace" form, shared by the main portal's sign-up page
// (frontend/src/features/auth/SignUpPage.tsx) and the admin portal's platform console
// (admin/client/src/features/tenants/CreateTenantForm.tsx). The admin API checks the same rules
// (Recuro.Admin.Domain.Tenants.WorkspaceForm) and its tests read definition.json, so the three cannot drift.
//
// Keep this file free of imports from node_modules: both apps compile it from here.
import definition from './definition.json'
import labels from './en.json'

export type WorkspaceOrgType = 'smallBusiness' | 'agency' | 'enterprise'
export type WorkspaceOwnerRole = 'hrta' | 'hrhead' | 'mdceo'
export type WorkspaceFormField = 'orgType' | 'orgName' | 'adminName' | 'adminRole' | 'email' | 'password'
export type PasswordRule = 'length' | 'upper' | 'lower' | 'digit' | 'symbol'

export interface WorkspaceFormDefinition {
  /** Fields in display order. */
  fields: readonly WorkspaceFormField[]
  orgTypes: readonly WorkspaceOrgType[]
  defaultOrgType: WorkspaceOrgType
  ownerRoles: readonly WorkspaceOwnerRole[]
  /** The owner's role starts here for each org type; the person can change it. */
  defaultOwnerRole: Readonly<Record<WorkspaceOrgType, WorkspaceOwnerRole>>
  orgNameMaxLength: number
  adminNameMaxLength: number
  slugMaxLength: number
  passwordMinLength: number
}

// definition.json is plain data; workspaceForm.test.ts in each app checks it fits these types.
export const WORKSPACE_FORM = definition as WorkspaceFormDefinition

/** English labels, registered by both apps under the `workspaceForm` i18n key. */
export const workspaceFormLabels = labels

/** What a workspace starts with: its details and the owner's account. */
export interface WorkspaceFormValues {
  orgType: WorkspaceOrgType
  orgName: string
  adminName: string
  adminRole: WorkspaceOwnerRole
  email: string
  password: string
}

export function emptyWorkspaceForm(): WorkspaceFormValues {
  const orgType = WORKSPACE_FORM.defaultOrgType
  return { orgType, orgName: '', adminName: '', adminRole: WORKSPACE_FORM.defaultOwnerRole[orgType], email: '', password: '' }
}

/** Changing the org type resets the owner's role to that type's default. */
export function withOrgType<T extends WorkspaceFormValues>(values: T, orgType: WorkspaceOrgType): T {
  return { ...values, orgType, adminRole: WORKSPACE_FORM.defaultOwnerRole[orgType] }
}

const PASSWORD_RULES: Record<PasswordRule, (pw: string) => boolean> = {
  length: (pw) => pw.length >= WORKSPACE_FORM.passwordMinLength,
  upper: (pw) => /[A-Z]/.test(pw),
  lower: (pw) => /[a-z]/.test(pw),
  digit: (pw) => /\d/.test(pw),
  symbol: (pw) => /[^A-Za-z0-9]/.test(pw),
}

export const PASSWORD_RULE_ORDER = Object.keys(PASSWORD_RULES) as PasswordRule[]

/** Rules the password still fails, in display order. Empty means it is acceptable (RCU-PLT-001). */
export function unmetPasswordRules(password: string): PasswordRule[] {
  return PASSWORD_RULE_ORDER.filter((rule) => !PASSWORD_RULES[rule](password))
}

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

export function isValidEmail(email: string): boolean {
  return EMAIL_PATTERN.test(email.trim())
}

/** `Aurora Housing Finance` → `aurora-housing-finance`. The admin API applies the same rule (WorkspaceSlug). */
export function workspaceSlug(name: string): string {
  return name
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '') // drop accents: Ü → U
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, WORKSPACE_FORM.slugMaxLength)
    .replace(/-+$/, '')
}

/** Problems with the values, as `workspaceForm.errors.<field>` keys. Empty means the form can be sent. */
export type WorkspaceFormErrors = Partial<Record<WorkspaceFormField, string>>

export function validateWorkspaceForm(values: WorkspaceFormValues): WorkspaceFormErrors {
  const errors: WorkspaceFormErrors = {}
  const orgName = values.orgName.trim()
  const adminName = values.adminName.trim()
  if (!WORKSPACE_FORM.orgTypes.includes(values.orgType)) errors.orgType = 'workspaceForm.errors.orgType'
  if (!orgName || orgName.length > WORKSPACE_FORM.orgNameMaxLength) errors.orgName = 'workspaceForm.errors.orgName'
  if (!adminName || adminName.length > WORKSPACE_FORM.adminNameMaxLength) errors.adminName = 'workspaceForm.errors.adminName'
  if (!WORKSPACE_FORM.ownerRoles.includes(values.adminRole)) errors.adminRole = 'workspaceForm.errors.adminRole'
  if (!isValidEmail(values.email)) errors.email = 'workspaceForm.errors.email'
  if (unmetPasswordRules(values.password).length > 0) errors.password = 'workspaceForm.errors.password'
  return errors
}

/** Values for the `{{max}}` and `{{count}}` placeholders in the shared labels. */
export const WORKSPACE_FORM_LIMITS: Partial<Record<WorkspaceFormField, Record<string, number>>> = {
  orgName: { max: WORKSPACE_FORM.orgNameMaxLength },
  adminName: { max: WORKSPACE_FORM.adminNameMaxLength },
  password: { count: WORKSPACE_FORM.passwordMinLength },
}
