import { credentialHeaders } from '../auth/credentials'
import { ApiError, type AdminApi } from './contract'

const BASE = '/api/v1'
export const PERSONA_HEADER = 'X-Recuro-Persona'
const NO_CONTENT = 204

interface ProblemDetails {
  title?: string
  code?: string
}

/** Fetch-based AdminApi. getCredentials supplies the Keycloak bearer token or the development persona header. */
export function createHttpClient(
  getCredentials: () => Promise<Record<string, string>> = credentialHeaders,
  fetchImpl: typeof fetch = fetch,
): AdminApi {
  async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
    const headers: Record<string, string> = { Accept: 'application/json', ...(await getCredentials()) }
    if (body !== undefined) headers['Content-Type'] = 'application/json'

    const response = await fetchImpl(`${BASE}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    })

    if (!response.ok) {
      const problem = (await response.json().catch(() => ({}))) as ProblemDetails
      throw new ApiError(response.status, problem.title ?? response.statusText, problem.code)
    }

    if (response.status === NO_CONTENT) return undefined as T
    return (await response.json()) as T
  }

  const key = encodeURIComponent
  const workspace = (scope: string | undefined) => (scope ? `/platform/workspaces/${key(scope)}` : '/workspace')

  return {
    getAuthConfig: () => request('GET', '/auth/config'),
    getMe: () => request('GET', '/auth/me'),
    listPersonas: () => request('GET', '/dev/personas'),
    listTenants: () => request('GET', '/platform/workspaces'),
    createTenant: (body) => request('POST', '/platform/workspaces', body),
    updateTenant: (id, body) => request('PUT', `/platform/workspaces/${key(id)}`, body),
    setTenantSuspended: (id, suspended) =>
      request('PUT', `/platform/workspaces/${key(id)}/status`, { status: suspended ? 'suspended' : 'active' }),
    assignTenantTheme: (id, body) => request('PUT', `/platform/workspaces/${key(id)}/theme`, body),
    listThemes: () => request('GET', '/platform/themes'),
    setThemePublished: (themeKey, isPublished) =>
      request('PUT', `/platform/themes/${key(themeKey)}/published`, { isPublished }),
    getBranding: () => request('GET', '/workspace/branding'),
    listScreens: () => request('GET', '/workspace/screens'),
    getScreen: (screenKey) => request('GET', `/workspace/screens/${key(screenKey)}`),
    updateScreen: (screenKey, body) => request('PUT', `/workspace/screens/${key(screenKey)}`, body),
    getRoleCatalog: (scope) => request('GET', scope ? '/platform/role-catalog' : '/workspace/roles/catalog'),
    listRoles: (scope) => request('GET', `${workspace(scope)}/roles`),
    createRole: (scope, body) => request('POST', `${workspace(scope)}/roles`, body),
    updateRole: (scope, roleKey, body) => request('PUT', `${workspace(scope)}/roles/${key(roleKey)}`, body),
    setRoleActive: (scope, roleKey, isActive) => request('PUT', `${workspace(scope)}/roles/${key(roleKey)}/status`, { isActive }),
    listMembers: (scope) => request('GET', `${workspace(scope)}/members`),
    updateMember: (scope, accountId, body) => request('PUT', `${workspace(scope)}/members/${key(accountId)}`, body),
    createMember: (scope, body) => request('POST', `${workspace(scope)}/members`, body),
    resetMemberPassword: (scope, accountId, body) => request('PUT', `${workspace(scope)}/members/${key(accountId)}/password`, body),
  }
}
