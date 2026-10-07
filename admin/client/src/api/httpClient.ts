import { credentialHeaders } from '../auth/credentials'
import { ApiError, type AdminApi } from './contract'

const BASE = '/api/v1'
export const PERSONA_HEADER = 'X-Recuro-Persona'

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

    return (await response.json()) as T
  }

  const key = encodeURIComponent

  return {
    getAuthConfig: () => request('GET', '/auth/config'),
    getMe: () => request('GET', '/auth/me'),
    listPersonas: () => request('GET', '/dev/personas'),
    listTenants: () => request('GET', '/platform/tenants'),
    createTenant: (body) => request('POST', '/platform/tenants', body),
    updateTenant: (id, body) => request('PUT', `/platform/tenants/${key(id)}`, body),
    setTenantSuspended: (id, suspended) =>
      request('PUT', `/platform/tenants/${key(id)}/status`, { status: suspended ? 'suspended' : 'active' }),
    assignTenantTheme: (id, body) => request('PUT', `/platform/tenants/${key(id)}/theme`, body),
    listThemes: () => request('GET', '/platform/themes'),
    setThemePublished: (themeKey, isPublished) =>
      request('PUT', `/platform/themes/${key(themeKey)}/published`, { isPublished }),
    getBranding: () => request('GET', '/tenant/branding'),
    listScreens: () => request('GET', '/tenant/screens'),
    getScreen: (screenKey) => request('GET', `/tenant/screens/${key(screenKey)}`),
    updateScreen: (screenKey, body) => request('PUT', `/tenant/screens/${key(screenKey)}`, body),
  }
}
