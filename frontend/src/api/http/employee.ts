// HTTP client for the Employee portal API (RCU-EMP-001/003/005) through the gateway. In development Vite
// proxies /api/v1/employee to the gateway, so the base URL is empty; set VITE_EMPLOYEE_API_URL to call another host.
import type { InternalJob, MyReferral, ReferralInput } from '../../domain/types'
import { ApiError, type EmployeeApi } from '../contract'

export const EMPLOYEE_SERVER_DOWN = 'We couldn’t reach the Recuro server for your workspace’s employee portal. Please try again in a few minutes.'
const SESSION_ENDED = 'Your session has ended. Please sign in again.'
const NOT_ALLOWED = 'Your role in this workspace can’t use the employee portal.'

/** Statuses a proxy or gateway returns when the service behind it is not running. */
const UNREACHABLE = new Set([500, 502, 503, 504])

/** The `title` of an RFC 7807 problem response is the user-facing message. */
async function problemMessage(res: Response, fallback: string): Promise<string> {
  try {
    const body: unknown = await res.json()
    if (typeof body === 'object' && body !== null && 'title' in body && typeof body.title === 'string') return body.title
  } catch {
    // Not JSON (for example a proxy error page).
  }
  return fallback
}

/**
 * `accessToken` returns the signed-in person's identity-provider token, or null when there is none.
 */
export function createHttpEmployeeApi(
  baseUrl: string,
  accessToken: () => Promise<string | null>,
  fetcher: typeof fetch = (...args) => fetch(...args),
): EmployeeApi {
  async function call(path: string, init: RequestInit = {}): Promise<Response> {
    const bearer = await accessToken()
    if (!bearer) throw new ApiError(401, SESSION_ENDED)
    let res: Response
    try {
      res = await fetcher(`${baseUrl}/api/v1/employee${path}`, {
        ...init,
        headers: { Accept: 'application/json', Authorization: `Bearer ${bearer}`, ...init.headers },
      })
    } catch {
      throw new ApiError(503, EMPLOYEE_SERVER_DOWN)
    }
    if (UNREACHABLE.has(res.status)) throw new ApiError(503, EMPLOYEE_SERVER_DOWN)
    if (res.status === 401) throw new ApiError(401, SESSION_ENDED)
    if (res.status === 403) throw new ApiError(403, NOT_ALLOWED)
    if (!res.ok) throw new ApiError(res.status, await problemMessage(res, EMPLOYEE_SERVER_DOWN))
    return res
  }

  async function json<T>(res: Response): Promise<T> {
    try {
      return (await res.json()) as T
    } catch {
      // A dev server without the proxy answers with its HTML page.
      throw new ApiError(503, EMPLOYEE_SERVER_DOWN)
    }
  }

  return {
    listInternalJobs: async () => json<InternalJob[]>(await call('/ijp')),
    listMyReferrals: async () => json<MyReferral[]>(await call('/me/referrals')),
    async submitReferral(_token, input: ReferralInput) {
      const res = await call('/referrals', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          reqId: input.reqId,
          name: input.name.trim(),
          email: input.email.trim(),
          phone: input.phone.trim() || null,
          experienceYears: input.experienceYears,
          relationship: input.relationship.trim(),
          coiAccepted: input.coiAccepted,
        }),
      })
      return json<MyReferral>(res)
    },
  }
}
