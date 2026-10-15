// DTOs returned by the admin API (admin/server). Enums serialise as camelCase strings.
import type { WorkspaceFormValues, WorkspaceOrgType } from '@workspace-form'

export type TenantKind = 'inHouse' | 'agency'
export type TenantPlan = 'starter' | 'professional' | 'enterprise'
export type TenantStatus = 'active' | 'suspended'
export type ThemeMode = 'system' | 'light' | 'dark'
export type FieldDataType =
  | 'text'
  | 'longText'
  | 'number'
  | 'currency'
  | 'date'
  | 'select'
  | 'email'
  | 'phone'
  | 'file'

export const TENANT_PLANS: TenantPlan[] = ['starter', 'professional', 'enterprise']
export const THEME_MODES: ThemeMode[] = ['system', 'light', 'dark']

export interface Tenant {
  id: string
  name: string
  slug: string
  /** Derived from orgType: an agency, or in-house HR. */
  kind: TenantKind
  orgType: WorkspaceOrgType
  emailDomain: string | null
  plan: TenantPlan
  status: TenantStatus
  customDomain: string | null
  themePresetKey: string
  themeMode: ThemeMode
  createdAt: string
}

/**
 * The shared create-workspace fields (admin/contracts/workspace-form, the same as the main portal's sign-up page),
 * plus the platform console's own settings. The password is temporary: the owner changes it at first sign-in.
 */
export interface CreateTenantRequest extends WorkspaceFormValues {
  plan: TenantPlan
  customDomain: string | null
}

export interface UpdateTenantRequest {
  name: string
  plan: TenantPlan
  customDomain: string | null
}

export interface ThemePalette {
  primary: string
  secondary: string
  accent: string
  background: string
  surface: string
  text: string
}

export interface ThemePreset {
  key: string
  name: string
  light: ThemePalette
  dark: ThemePalette
  isPublished: boolean
}

/** A tenant admin's view of their theme. The platform console assigns it; tenants can't change it. */
export interface Branding {
  tenantName: string
  themePresetKey: string
  themeMode: ThemeMode
  theme: ThemePreset
}

export interface AssignTenantThemeRequest {
  themePresetKey: string
  themeMode: ThemeMode
}

export interface EffectiveField {
  key: string
  label: string
  defaultLabel: string
  dataType: FieldDataType
  isVisible: boolean
  isRequired: boolean
  isLocked: boolean
  sortOrder: number
  defaultRequired: boolean
  defaultSortOrder: number
}

export interface EffectiveScreen {
  key: string
  code: string
  module: string
  isEnabled: boolean
  canDisable: boolean
  title: string
  subtitle: string
  defaultTitle: string
  defaultSubtitle: string
  fields: EffectiveField[]
}

export interface UpdateFieldRequest {
  key: string
  label: string | null
  isVisible: boolean
  isRequired: boolean
  sortOrder: number
}

export interface UpdateScreenRequest {
  isEnabled: boolean
  title: string | null
  subtitle: string | null
  fields: UpdateFieldRequest[]
}

/** How the API expects people to sign in: Keycloak (OIDC) or the local development personas. */
export interface AuthConfig {
  mode: 'oidc' | 'development'
  authority: string | null
  clientId: string | null
}

export type AdminLevel = 'platform' | 'tenant'

/** The signed-in admin, as the API sees them. */
export interface CurrentAdmin {
  name: string
  level: AdminLevel
  tenantId: string | null
  tenantName: string | null
}

/** Development sign-in persona (stands in for Keycloak when running without it). */
export interface Persona {
  id: string
  role: string
  tenantName: string | null
}

/** One action a role can be allowed, from the product's permission catalogue. */
export interface PermissionDefinition {
  key: string
  module: string
  label: string
}

/** A screen a role can be given access to. */
export interface RolePage {
  key: string
  code: string
  title: string
  module: string
}

export interface RoleCatalog {
  permissions: PermissionDefinition[]
  pages: RolePage[]
}

/** A workspace role, defined per tenant in the database (not hard-coded). */
export interface WorkspaceRole {
  key: string
  name: string
  description: string | null
  /** Key of the role this one reports to, or null at the top. */
  reportsTo: string | null
  permissions: string[]
  pages: string[]
  requiresMfa: boolean
  isSystem: boolean
  isActive: boolean
  memberCount: number
}

export interface UpdateRoleRequest {
  name: string
  description: string | null
  reportsTo: string | null
  permissions: string[]
  pages: string[]
  requiresMfa: boolean
}

export interface CreateRoleRequest extends UpdateRoleRequest {
  key: string
}

/** A person in the workspace. accountId is the Keycloak subject. */
export interface WorkspaceMember {
  accountId: string
  name: string
  email: string
  roleKeys: string[]
  department: string | null
}

export interface UpdateMemberRequest {
  roleKeys: string[]
  department: string | null
}

/**
 * Which workspace a roles call acts on: a tenant admin's own (no id), or the one a platform admin picked.
 */
export type RoleScope = string | undefined
