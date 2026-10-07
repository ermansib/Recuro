import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import type { Branding } from '../../api/types'
import { fakeApi, PRESETS, renderWithApi } from '../../test/render'
import { BrandingPage } from './BrandingPage'

const BRANDING: Branding = { tenantName: 'Aurora Housing Finance', themePresetKey: 'recuro-classic', themeMode: 'system', presets: PRESETS }

describe('BrandingPage', () => {
  it('saves the chosen preset and mode', async () => {
    const updateBranding = vi.fn((request) => Promise.resolve({ ...BRANDING, ...request }))
    renderWithApi(<BrandingPage />, { api: fakeApi({ getBranding: () => Promise.resolve(BRANDING), updateBranding }) })

    const save = await screen.findByRole('button', { name: 'Save changes' })
    expect(save).toBeDisabled()

    await userEvent.click(screen.getByRole('radio', { name: /Royal Plum/ }))
    await userEvent.click(screen.getByRole('radio', { name: 'Dark' }))
    expect(screen.getByText('You have unsaved changes.')).toBeInTheDocument()

    await userEvent.click(save)
    await waitFor(() => expect(updateBranding).toHaveBeenCalledWith({ themePresetKey: 'royal-plum', themeMode: 'dark' }))
    expect(await screen.findByText(/Theme saved/)).toBeInTheDocument()
  })

  it('moves between presets with the arrow keys', async () => {
    renderWithApi(<BrandingPage />, { api: fakeApi({ getBranding: () => Promise.resolve(BRANDING) }) })

    const classic = await screen.findByRole('radio', { name: /Recuro Classic/ })
    expect(classic).toHaveAttribute('aria-checked', 'true')
    classic.focus()
    await userEvent.keyboard('{ArrowRight}')

    const plum = screen.getByRole('radio', { name: /Royal Plum/ })
    expect(plum).toHaveAttribute('aria-checked', 'true')
    expect(plum).toHaveFocus()
  })

  it('switches the preview palette to dark', async () => {
    renderWithApi(<BrandingPage />, { api: fakeApi({ getBranding: () => Promise.resolve(BRANDING) }) })

    await userEvent.click(await screen.findByRole('button', { name: 'Dark' }))

    expect(screen.getByText('#A5B4FC #22D3EE #A78BFA')).toBeInTheDocument()
  })
})
