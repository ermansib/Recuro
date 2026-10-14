// The one seam between the UI and the backend. Hiring data still comes from an in-memory mock seeded
// from static JSON (src/mocks/data). Workspaces and sign-in are real: the admin portal's API saves and
// looks up workspaces, and Keycloak signs their people in. When the .NET services exist, add HTTP
// implementations of the rest of ApiClient and switch `api` below; no component imports mock data directly.
import { tenant as defaultTenant } from '../config/tenant'
import type { ApiClient, AppearanceApi } from './contract'
import { createHttpAppearanceApi } from './http/appearance'
import { createIdentityProviderAuthApi } from './http/identity'
import { createKeycloakIdentityProvider } from './http/keycloak'
import { createHttpWorkspaceApi } from './http/workspaces'
import { createMockClient } from './mock/mockClient'

export * from './contract'

/** Empty means same origin; in development Vite proxies the admin API (see vite.config.ts). */
const adminApiUrl = import.meta.env.VITE_ADMIN_API_URL ?? ''

const mock = createMockClient()

export const api: ApiClient = {
  ...mock,
  ...createIdentityProviderAuthApi({
    local: mock,
    workspaces: createHttpWorkspaceApi(adminApiUrl),
    provider: createKeycloakIdentityProvider(),
    defaults: defaultTenant,
  }),
}

/** Tenant themes come from the admin portal (admin/), proxied in development (see vite.config.ts). */
export const appearanceApi: AppearanceApi = createHttpAppearanceApi(adminApiUrl)
