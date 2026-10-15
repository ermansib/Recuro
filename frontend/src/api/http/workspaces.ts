// HTTP client for the admin portal's workspace endpoints. Sign-up is real: the workspace is saved in the
// admin database (table `tenants`) and the owner's account is created in Keycloak. The lookup tells the
// sign-in page which workspace it is signing in to. In development Vite proxies /api/v1/workspaces to the
// admin API, so the base URL is empty (see vite.config.ts).
import type { MyAccess, RegisteredWorkspace, Workspace } from '../../domain/types'
import { ApiError, type WorkspaceApi } from '../contract'

export const SIGN_UP_SERVER_DOWN = 'We couldn’t reach the Recuro server to create your workspace. Please try again in a few minutes.'
export const ACCESS_SERVER_DOWN = 'We couldn’t reach the Recuro server to read your roles. Please try again in a few minutes.'
export const ACCESS_REFUSED =
  'The Recuro server didn’t accept your sign-in. Please sign in again, and ask your administrator to check the sign-in settings if this keeps happening.'
export const LOOKUP_SERVER_DOWN = 'We couldn’t reach the Recuro server to open that workspace. Please try again in a few minutes.'

/** Statuses a proxy or gateway returns when the admin API itself is not running. */
const UNREACHABLE = new Set([500, 502, 504])

/** Reads the `title` of an RFC 7807 problem response, which is the user-facing message. */
async function problemMessage(res: Response, fallback: string): Promise<string> {
  try {
    const body: unknown = await res.json()
    if (typeof body === 'object' && body !== null && 'title' in body && typeof body.title === 'string') return body.title
  } catch {
    // Not JSON (for example a proxy error page): fall through to the generic message.
  }
  return fallback
}

export function createHttpWorkspaceApi(baseUrl: string, fetcher: typeof fetch = (...args) => fetch(...args)): WorkspaceApi {
  /** Calls the admin API; a network failure or a dead upstream becomes a 503 with `serverDown`. */
  async function send(path: string, init: RequestInit, serverDown: string): Promise<Response> {
    let res: Response
    try {
      res = await fetcher(`${baseUrl}${path}`, { ...init, headers: { Accept: 'application/json', ...init.headers } })
    } catch {
      throw new ApiError(503, serverDown)
    }
    if (UNREACHABLE.has(res.status)) throw new ApiError(503, serverDown)
    return res
  }

  return {
    async registerWorkspace(input) {
      const res = await send(
        '/api/v1/workspaces',
        { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) },
        SIGN_UP_SERVER_DOWN,
      )
      if (res.status === 429) throw new ApiError(429, 'Too many sign-ups from this network. Please try again later.')
      if (!res.ok) throw new ApiError(res.status, await problemMessage(res, SIGN_UP_SERVER_DOWN))
      return (await res.json()) as RegisteredWorkspace
    },

    async getWorkspace(slug) {
      const res = await send(`/api/v1/workspaces/${encodeURIComponent(slug)}`, { method: 'GET' }, LOOKUP_SERVER_DOWN)
      if (res.status === 404) return null
      if (!res.ok) throw new ApiError(res.status, await problemMessage(res, LOOKUP_SERVER_DOWN))
      try {
        return (await res.json()) as Workspace
      } catch {
        // A dev server without the proxy answers with its HTML page: treat it as no admin API.
        throw new ApiError(503, LOOKUP_SERVER_DOWN)
      }
    },

    async getMyAccess(accessToken) {
      const res = await send('/api/v1/me/access', { method: 'GET', headers: { Authorization: `Bearer ${accessToken}` } }, ACCESS_SERVER_DOWN)
      // A refused token answers with a bare "Unauthorized" problem, which tells the person nothing.
      if (res.status === 401) throw new ApiError(401, ACCESS_REFUSED)
      if (!res.ok) throw new ApiError(res.status, await problemMessage(res, ACCESS_SERVER_DOWN))
      try {
        return (await res.json()) as MyAccess
      } catch {
        throw new ApiError(503, ACCESS_SERVER_DOWN)
      }
    },
  }
}
