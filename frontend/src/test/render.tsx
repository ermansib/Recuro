import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import type { ReactElement } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { SessionProvider } from '../auth/session'
import { ToastProvider } from '../components/Toasts'
import type { Role } from '../domain/types'
import users from '../mocks/data/users.json'
import type { User } from '../domain/types'

export function renderWithProviders(ui: ReactElement, { role = 'hrta', route = '/' }: { role?: Role; route?: string } = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[route]}>
        <SessionProvider users={users as User[]} initialRole={role}>
          <ToastProvider>{ui}</ToastProvider>
        </SessionProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}
