import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import type { ReactElement } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ApiContext } from '../api/client'
import type { AdminApi } from '../api/contract'
import type { CurrentAdmin, EffectiveScreen, ThemePreset } from '../api/types'
import { SessionProvider } from '../auth/SessionProvider'
import type { AuthStrategy } from '../auth/strategies'

export const TENANT_ADMIN: CurrentAdmin = {
  name: 'Aurora Admin',
  level: 'tenant',
  tenantId: '6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a01',
  tenantName: 'Aurora Housing Finance',
}

/** A strategy that never signs anyone in by itself; tests pass initialAdmin or drive signIn. */
export function fakeStrategy(overrides: Partial<AuthStrategy> = {}): AuthStrategy {
  return {
    mode: 'development',
    restore: () => Promise.resolve(false),
    headers: () => Promise.resolve({}),
    signIn: () => Promise.resolve(),
    signOut: () => Promise.resolve(),
    ...overrides,
  }
}

export const PRESETS: ThemePreset[] = [
  {
    key: 'recuro-classic',
    name: 'Recuro Classic',
    isPublished: true,
    light: { primary: '#1E2A5E', secondary: '#22D3EE', accent: '#7C3AED', background: '#F4F6FB', surface: '#FFFFFF', text: '#111827' },
    dark: { primary: '#A5B4FC', secondary: '#22D3EE', accent: '#A78BFA', background: '#0B1020', surface: '#151C36', text: '#E5E7EB' },
  },
  {
    key: 'royal-plum',
    name: 'Royal Plum',
    isPublished: true,
    light: { primary: '#4C1D95', secondary: '#C4B5FD', accent: '#DB2777', background: '#F7F5FC', surface: '#FFFFFF', text: '#111827' },
    dark: { primary: '#C4B5FD', secondary: '#A78BFA', accent: '#F472B6', background: '#120B1F', surface: '#1E1433', text: '#EDE9FE' },
  },
]

export const MRF_SCREEN: EffectiveScreen = {
  key: 'mrf',
  code: 'S-02',
  module: 'Requisitions',
  isEnabled: true,
  canDisable: false,
  title: 'New manpower requisition',
  subtitle: 'Raise demand',
  defaultTitle: 'New manpower requisition',
  defaultSubtitle: 'Raise demand',
  fields: [
    { key: 'grade', label: 'Grade', defaultLabel: 'Grade', dataType: 'select', isVisible: true, isRequired: true, isLocked: true, sortOrder: 10, defaultRequired: true, defaultSortOrder: 10 },
    { key: 'location', label: 'Location', defaultLabel: 'Location', dataType: 'select', isVisible: true, isRequired: true, isLocked: false, sortOrder: 20, defaultRequired: true, defaultSortOrder: 20 },
    { key: 'band', label: 'Salary band', defaultLabel: 'Salary band', dataType: 'select', isVisible: true, isRequired: false, isLocked: false, sortOrder: 30, defaultRequired: false, defaultSortOrder: 30 },
  ],
}

/** A fake AdminApi: every method rejects unless the test overrides it. */
export function fakeApi(overrides: Partial<AdminApi> = {}): AdminApi {
  const notStubbed = (name: string) => () => Promise.reject(new Error(`${name} not stubbed`))
  const methods: (keyof AdminApi)[] = [
    'getAuthConfig', 'getMe', 'listPersonas', 'listTenants', 'createTenant', 'updateTenant', 'setTenantSuspended', 'assignTenantTheme',
    'listThemes', 'setThemePublished', 'getBranding', 'listScreens', 'getScreen', 'updateScreen',
  ]
  return Object.fromEntries(methods.map((name) => [name, overrides[name] ?? notStubbed(name)])) as unknown as AdminApi
}

export function renderWithApi(
  ui: ReactElement,
  {
    api,
    route = '/',
    path = '*',
    admin = TENANT_ADMIN,
    strategy = fakeStrategy(),
  }: { api: AdminApi; route?: string; path?: string; admin?: CurrentAdmin | null; strategy?: AuthStrategy },
) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <ApiContext.Provider value={api}>
        <MemoryRouter initialEntries={[route]}>
          <SessionProvider strategy={strategy} initialAdmin={admin ?? undefined}>
            <Routes>
              <Route path={path} element={ui} />
            </Routes>
          </SessionProvider>
        </MemoryRouter>
      </ApiContext.Provider>
    </QueryClientProvider>,
  )
}
