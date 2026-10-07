import type {
  AssignTenantThemeRequest,
  AuthConfig,
  Branding,
  CurrentAdmin,
  CreateTenantRequest,
  EffectiveScreen,
  Persona,
  Tenant,
  ThemePreset,
  UpdateScreenRequest,
  UpdateTenantRequest,
} from './types'

/** The one seam between the admin client and the admin API. Components use it through hooks.ts. */
export interface AdminApi {
  // sign-in
  getAuthConfig(): Promise<AuthConfig>
  getMe(): Promise<CurrentAdmin>
  listPersonas(): Promise<Persona[]>

  // platform console
  listTenants(): Promise<Tenant[]>
  createTenant(request: CreateTenantRequest): Promise<Tenant>
  updateTenant(id: string, request: UpdateTenantRequest): Promise<Tenant>
  setTenantSuspended(id: string, suspended: boolean): Promise<Tenant>
  /** Gives one tenant a theme from the library; the main portal shows it (RCU-PLT-006). */
  assignTenantTheme(id: string, request: AssignTenantThemeRequest): Promise<Tenant>
  listThemes(): Promise<ThemePreset[]>
  setThemePublished(key: string, isPublished: boolean): Promise<ThemePreset>

  // tenant admin
  getBranding(): Promise<Branding>
  listScreens(): Promise<EffectiveScreen[]>
  getScreen(key: string): Promise<EffectiveScreen>
  updateScreen(key: string, request: UpdateScreenRequest): Promise<EffectiveScreen>
}

/** An RFC 7807 problem returned by the API, with the domain error code (for example "screen.lockedField"). */
export class ApiError extends Error {
  readonly status: number
  readonly code: string | undefined

  constructor(status: number, message: string, code?: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
  }
}
