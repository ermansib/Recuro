import type {
  AssignTenantThemeRequest,
  AuthConfig,
  Branding,
  CurrentAdmin,
  CreateMemberRequest,
  CreateRoleRequest,
  CreateTenantRequest,
  EffectiveScreen,
  Persona,
  ResetMemberPasswordRequest,
  RoleCatalog,
  RoleScope,
  Tenant,
  ThemePreset,
  UpdateMemberRequest,
  UpdateRoleRequest,
  UpdateScreenRequest,
  UpdateTenantRequest,
  WorkspaceMember,
  WorkspaceRole,
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

  // workspace roles and members: tenant admins (scope undefined) and platform admins (scope = tenant id)
  getRoleCatalog(scope: RoleScope): Promise<RoleCatalog>
  listRoles(scope: RoleScope): Promise<WorkspaceRole[]>
  createRole(scope: RoleScope, request: CreateRoleRequest): Promise<WorkspaceRole>
  updateRole(scope: RoleScope, key: string, request: UpdateRoleRequest): Promise<WorkspaceRole>
  setRoleActive(scope: RoleScope, key: string, isActive: boolean): Promise<WorkspaceRole>
  listMembers(scope: RoleScope): Promise<WorkspaceMember[]>
  updateMember(scope: RoleScope, accountId: string, request: UpdateMemberRequest): Promise<WorkspaceMember>
  /** Creates the person's sign-in account with a temporary password (email isn't set up yet, so no invite). */
  createMember(scope: RoleScope, request: CreateMemberRequest): Promise<WorkspaceMember>
  /** Sets a temporary password the person must replace at their next sign-in. */
  resetMemberPassword(scope: RoleScope, accountId: string, request: ResetMemberPasswordRequest): Promise<void>
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
