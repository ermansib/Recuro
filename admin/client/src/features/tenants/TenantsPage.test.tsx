import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { CurrentAdmin, Tenant } from '../../api/types'
import { fakeApi, renderWithApi } from '../../test/render'
import { TenantsPage } from './TenantsPage'

const PLATFORM: CurrentAdmin = { name: 'Recuro Platform', level: 'platform', tenantId: null, tenantName: null }
const AURORA: Tenant = {
  id: 't-1',
  name: 'Aurora Housing Finance',
  slug: 'aurora-housing-finance',
  kind: 'inHouse',
  orgType: 'enterprise',
  emailDomain: null,
  plan: 'enterprise',
  status: 'active',
  customDomain: null,
  themePresetKey: 'recuro-classic',
  themeMode: 'system',
  createdAt: '2026-10-01T00:00:00Z',
}

describe('Workspaces page', () => {
  it('opens the add-a-workspace form from the page header and closes it again', async () => {
    const user = userEvent.setup()
    renderWithApi(<TenantsPage />, { api: fakeApi({ listTenants: () => Promise.resolve([AURORA]) }), admin: PLATFORM })

    expect(await screen.findByText('Aurora Housing Finance')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Add a workspace' })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Add workspace' }))
    expect(screen.getByRole('heading', { name: 'Add a workspace' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('heading', { name: 'Add a workspace' })).not.toBeInTheDocument()
  })

  it('shows the form straight away when there are no workspaces yet', async () => {
    renderWithApi(<TenantsPage />, { api: fakeApi({ listTenants: () => Promise.resolve([]) }), admin: PLATFORM })

    expect(await screen.findByRole('heading', { name: 'Add a workspace' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Close' })).not.toBeInTheDocument()
  })
})
