import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import { App } from '../../App'
import { ApiError } from '../../api/contract'
import { fakeApi, fakeStrategy, renderWithApi } from '../../test/render'

describe('sign-in', () => {
  it('sends people to Keycloak when the API uses OIDC', async () => {
    const signIn = vi.fn(() => Promise.resolve())
    renderWithApi(<App />, { api: fakeApi(), admin: null, strategy: fakeStrategy({ mode: 'oidc', signIn }) })

    await userEvent.click(await screen.findByRole('button', { name: 'Sign in' }))

    expect(signIn).toHaveBeenCalled()
  })

  it('restores a Keycloak session and opens the tenant admin', async () => {
    const getMe = vi.fn(() =>
      Promise.resolve({ name: 'Aurora Admin', level: 'tenant' as const, tenantId: 't1', tenantName: 'Aurora Housing Finance' }),
    )
    const getBranding = () => new Promise<never>(() => {})
    renderWithApi(<App />, {
      api: fakeApi({ getMe, getBranding }),
      admin: null,
      strategy: fakeStrategy({ mode: 'oidc', restore: () => Promise.resolve(true) }),
    })

    expect(await screen.findByRole('link', { name: 'Branding & theme' })).toBeInTheDocument()
    expect(screen.getByText('Aurora Admin')).toBeInTheDocument()
  })

  it('signs in as a development persona', async () => {
    const signIn = vi.fn(() => Promise.resolve())
    const listPersonas = () => Promise.resolve([{ id: 'platform', role: 'Platform admin', tenantName: null }])
    const getMe = () => Promise.resolve({ name: 'Platform admin', level: 'platform' as const, tenantId: null, tenantName: null })
    const listTenants = () => Promise.resolve([])
    renderWithApi(<App />, { api: fakeApi({ listPersonas, getMe, listTenants }), admin: null, strategy: fakeStrategy({ signIn }) })

    await userEvent.click(await screen.findByRole('button', { name: /Platform admin/ }))

    expect(signIn).toHaveBeenCalledWith('platform')
    expect(await screen.findByRole('link', { name: 'Tenants' })).toBeInTheDocument()
  })

  it('explains when the account has no admin role', async () => {
    const getMe = () => Promise.reject(new ApiError(403, 'This account has no Recuro admin role.'))
    renderWithApi(<App />, {
      api: fakeApi({ getMe }),
      admin: null,
      strategy: fakeStrategy({ mode: 'oidc', restore: () => Promise.resolve(true) }),
    })

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('no Recuro admin role'))
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument()
  })
})
