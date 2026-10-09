// The one seam between the UI and the backend. Today it is backed by an in-memory mock that is
// seeded from static JSON (src/mocks/data). When the .NET Core services exist, add an HTTP
// implementation of ApiClient and switch `api` below; no component imports mock data directly.
import type { ApiClient, AppearanceApi } from './contract'
import { createHttpWorkspaceApi } from './http/workspaces'
import { createHttpAppearanceApi } from './http/appearance'
import { createMockClient } from './mock/mockClient'

export * from './contract'

/** Workspaces are saved by the admin portal's API (PostgreSQL + Keycloak); the rest of `api` is still the mock. */
const adminApiUrl = import.meta.env.VITE_ADMIN_API_URL ?? ''

export const api: ApiClient = createMockClient(undefined, createHttpWorkspaceApi(adminApiUrl))

/** Tenant themes come from the admin portal (admin/), proxied in development (see vite.config.ts). */
export const appearanceApi: AppearanceApi = createHttpAppearanceApi(adminApiUrl)
