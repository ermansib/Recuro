// The one seam between the UI and the backend. Hiring data still comes from an in-memory mock seeded
// from static JSON (src/mocks/data). Workspaces and sign-in are real: the admin portal's API saves and
// looks up workspaces, and Keycloak signs their people in. When the .NET services exist, add HTTP
// implementations of the rest of ApiClient and switch `api` below; no component imports mock data directly.
import { tenant as defaultTenant } from '../config/tenant'
import type { ApiClient, AppearanceApi, EmployeeApi } from './contract'
import { createHttpAppearanceApi } from './http/appearance'
import { createHttpEmployeeApi } from './http/employee'
import { IDP_TOKEN_PREFIX, createIdentityProviderAuthApi } from './http/identity'
import { createKeycloakIdentityProvider } from './http/keycloak'
import { createHttpWorkspaceApi } from './http/workspaces'
import { createMockEmployeeApi } from './mock/employee'
import { createMockClient } from './mock/mockClient'

export * from './contract'

/** Empty means same origin; in development Vite proxies the admin API (see vite.config.ts). */
const adminApiUrl = import.meta.env.VITE_ADMIN_API_URL ?? ''

const mock = createMockClient()
const identityProvider = createKeycloakIdentityProvider()

export const api: ApiClient = {
  ...mock,
  ...createIdentityProviderAuthApi({
    local: mock,
    workspaces: createHttpWorkspaceApi(adminApiUrl),
    provider: identityProvider,
    defaults: defaultTenant,
  }),
}

/** Tenant themes come from the admin portal (admin/), proxied in development (see vite.config.ts). */
export const appearanceApi: AppearanceApi = createHttpAppearanceApi(adminApiUrl)

/** Empty means same origin; in development Vite proxies the gateway (see vite.config.ts). */
const employeeApiUrl = import.meta.env.VITE_EMPLOYEE_API_URL ?? ''

const httpEmployee = createHttpEmployeeApi(employeeApiUrl, async () => (await identityProvider.currentAccount())?.accessToken ?? null)
const mockEmployee = createMockEmployeeApi(mock.listJobPostings)
const forToken = (token: string): EmployeeApi => (token.startsWith(IDP_TOKEN_PREFIX) ? httpEmployee : mockEmployee)

/**
 * The Employee portal API (RCU-EMP). Sessions signed in through Keycloak call the real service through the
 * gateway; the in-browser demo workspace keeps the mock, so the demo personas still work offline.
 */
export const employeeApi: EmployeeApi = {
  listInternalJobs: (token) => forToken(token).listInternalJobs(token),
  submitReferral: (token, input) => forToken(token).submitReferral(token, input),
  listMyReferrals: (token) => forToken(token).listMyReferrals(token),
}
