// HTTP client for the admin portal's workspace endpoint. Sign-up is real: the workspace is saved in the
// admin database (table `tenants`) and the owner's account is created in Keycloak. In development Vite
// proxies /api/v1/workspaces to the admin API, so the base URL is empty (see vite.config.ts).
import type { RegisteredWorkspace } from '../../domain/types'
import { ApiError, type WorkspaceApi } from '../contract'

const SERVER_DOWN = 'We couldn’t reach the Recuro server to create your workspace. Please try again in a few minutes.'

/** Reads the `title` of an RFC 7807 problem response, which is the user-facing message. */
async function problemMessage(res: Response): Promise<string> {
  try {
    const body: unknown = await res.json()
    if (typeof body === 'object' && body !== null && 'title' in body && typeof body.title === 'string') return body.title
  } catch {
    // Not JSON (for example a proxy error page): fall through to the generic message.
  }
  return SERVER_DOWN
}

export function createHttpWorkspaceApi(baseUrl: string, fetcher: typeof fetch = (...args) => fetch(...args)): WorkspaceApi {
  return {
    async registerWorkspace(input) {
      let res: Response
      try {
        res = await fetcher(`${baseUrl}/api/v1/workspaces`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
          body: JSON.stringify(input),
        })
      } catch {
        throw new ApiError(503, SERVER_DOWN)
      }
      if (res.status === 429) throw new ApiError(429, 'Too many sign-ups from this network. Please try again later.')
      if (!res.ok) throw new ApiError(res.status, await problemMessage(res))
      return (await res.json()) as RegisteredWorkspace
    },
  }
}
