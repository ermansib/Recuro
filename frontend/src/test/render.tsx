import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import type { ReactElement } from 'react'
import { MemoryRouter } from 'react-router-dom'
import type { AppearanceApi } from '../api/contract'
import { AuthProvider } from '../auth/session'
import { ToastProvider } from '../components/Toasts'
import { ThemeProvider } from '../theme/ThemeProvider'
import type { AuthSession, Role, TenantConfig, User } from '../domain/types'
import tenant from '../mocks/data/tenant.json'
import users from '../mocks/data/users.json'

/** Tests run without the admin portal, as the portal does when it is down. */
const offlineAppearance: AppearanceApi = { getTenantAppearance: () => Promise.resolve(null) }

/** A signed-in session for a seeded persona, without going through the sign-in screen. */
export function demoSession(role: Role): AuthSession {
  const user = (users as User[]).find((u) => u.role === role)
  if (!user) throw new Error(`No seeded user for ${role}`)
  return { token: `test-${role}`, user, tenant: tenant as TenantConfig, issuedAt: new Date().toISOString() }
}

export function renderWithProviders(
  ui: ReactElement,
  { role = 'hrta', route = '/', signedIn = true }: { role?: Role; route?: string; signedIn?: boolean } = {},
) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[route]}>
        <ThemeProvider api={offlineAppearance}>
          <AuthProvider initialSession={signedIn ? demoSession(role) : undefined}>
            <ToastProvider>{ui}</ToastProvider>
          </AuthProvider>
        </ThemeProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}
