import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { Branding } from '../../api/types'
import { fakeApi, PRESETS, renderWithApi } from '../../test/render'
import { BrandingPage } from './BrandingPage'

const BRANDING: Branding = { tenantName: 'Aurora Housing Finance', themePresetKey: 'royal-plum', themeMode: 'dark', theme: PRESETS[1]! }

describe('BrandingPage', () => {
  it("shows the tenant's assigned theme without letting them change it", async () => {
    renderWithApi(<BrandingPage />, { api: fakeApi({ getBranding: () => Promise.resolve(BRANDING) }) })

    expect(await screen.findByText('Royal Plum')).toBeInTheDocument()
    expect(screen.getByText('Dark', { selector: 'dd' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /save/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('radio', { name: /Recuro Classic/ })).not.toBeInTheDocument()
  })

  it('previews the assigned theme in light and dark', async () => {
    renderWithApi(<BrandingPage />, { api: fakeApi({ getBranding: () => Promise.resolve(BRANDING) }) })

    expect(await screen.findByTitle('Primary #C4B5FD')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Light' }))
    expect(screen.getByTitle('Primary #4C1D95')).toBeInTheDocument()
  })
})
