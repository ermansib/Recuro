// DTOs returned by the admin API (admin/server). Enums serialise as camelCase strings.

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

export const TENANT_KINDS: TenantKind[] = ['inHouse', 'agency']
export const TENANT_PLANS: TenantPlan[] = ['starter', 'professional', 'enterprise']
export const THEME_MODES: ThemeMode[] = ['system', 'light', 'dark']

export interface Tenant {
  id: string
  name: string
  slug: string
  kind: TenantKind
  plan: TenantPlan
  status: TenantStatus
  customDomain: string | null
  themePresetKey: string
  themeMode: ThemeMode
  createdAt: string
}

export interface CreateTenantRequest {
  name: string
  slug: string
  kind: TenantKind
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

export interface Branding {
  tenantName: string
  themePresetKey: string
  themeMode: ThemeMode
  presets: ThemePreset[]
}

export interface UpdateBrandingRequest {
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
