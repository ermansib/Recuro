// The one seam between the UI and the backend. Today it is backed by an in-memory mock that is
// seeded from static JSON (src/mocks/data). When the .NET Core services exist, add an HTTP
// implementation of ApiClient and switch `api` below; no component imports mock data directly.
import type { ApiClient } from './contract'
import { createMockClient } from './mock/mockClient'

export * from './contract'

export const api: ApiClient = createMockClient()
