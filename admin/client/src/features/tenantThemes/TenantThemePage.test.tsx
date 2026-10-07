import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import type { CurrentAdmin, Tenant } from '../../api/types'
import { fakeApi, PRESETS, renderWithApi } from '../../test/render'
import { TenantThemePage } from './TenantThemePage'

const PLATFORM: CurrentAdmin = { name: 'Recuro Platform', level: 'platform', tenantId: null, tenantName: null }

const tenant = (id: string, name: string, slug: string): Tenant => ({
  id,
  name,
  slug,
  kind: 'inHouse',
  plan: 'enterprise',
  status: 'active',
  customDomain: null,
  themePresetKey: 'recuro-classic',
  themeMode: 'system',
  createdAt: '2026-10-07T00:00:00Z',
})

const TENANTS = [tenant('t-aurora', 'Aurora Housing Finance', 'aurora'), tenant('t-bridge', 'TalentBridge Staffing', 'talentbridge')]
const DRAFT = { ...PRESETS[0]!, key: 'neon', name: 'Neon Draft', isPublished: false }

function renderPage(route = '/platform/tenant-themes') {
  const assignTenantTheme = vi.fn((id: string, request: { themePresetKey: string; themeMode: Tenant['themeMode'] }) =>
    Promise.resolve({ ...TENANTS.find((t) => t.id === id)!, ...request }),
  )
  const api = fakeApi({
    listTenants: () => Promise.resolve(TENANTS),
    listThemes: () => Promise.resolve([...PRESETS, DRAFT]),
    assignTenantTheme,
  })
  renderWithApi(<TenantThemePage />, { api, route, admin: PLATFORM })
  return assignTenantTheme
}

describe('TenantThemePage', () => {
  it('assigns a theme and mode to the chosen tenant only', async () => {
    const assign = renderPage()

    await userEvent.selectOptions(await screen.findByLabelText('Tenant'), 'TalentBridge Staffing')
    const save = screen.getByRole('button', { name: 'Save theme for TalentBridge Staffing' })
    expect(save).toBeDisabled()

    await userEvent.click(screen.getByRole('radio', { name: /Royal Plum/ }))
    await userEvent.click(screen.getByRole('radio', { name: 'Dark' }))
    await userEvent.click(save)

    await waitFor(() => expect(assign).toHaveBeenCalledWith('t-bridge', { themePresetKey: 'royal-plum', themeMode: 'dark' }))
    expect(await screen.findByText(/Theme saved for TalentBridge Staffing/)).toBeInTheDocument()
  })

  it('opens on the tenant named in the link from the tenants list', async () => {
    renderPage('/platform/tenant-themes?tenant=t-bridge')
    expect(await screen.findByLabelText('Tenant')).toHaveValue('t-bridge')
  })

  it('offers only themes that are available in the library', async () => {
    renderPage()
    expect(await screen.findByRole('radio', { name: /Recuro Classic/ })).toBeInTheDocument()
    expect(screen.queryByRole('radio', { name: /Neon Draft/ })).not.toBeInTheDocument()
  })

  it('moves between presets with the arrow keys', async () => {
    renderPage()
    const classic = await screen.findByRole('radio', { name: /Recuro Classic/ })
    expect(classic).toHaveAttribute('aria-checked', 'true')
    classic.focus()
    await userEvent.keyboard('{ArrowRight}')

    const plum = screen.getByRole('radio', { name: /Royal Plum/ })
    expect(plum).toHaveAttribute('aria-checked', 'true')
    expect(plum).toHaveFocus()
  })
})
