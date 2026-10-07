import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it } from 'vitest'
import type { AppearanceApi } from '../api/contract'
import { ThemeSwitch } from '../components/ThemeSwitch'
import type { TenantAppearance } from '../domain/types'
import { THEME_MODE_STORAGE_KEY, ThemeProvider } from './ThemeProvider'
import { useTenantTheme } from './themeContext'

const emerald: TenantAppearance = {
  slug: 'aurora',
  themePresetKey: 'emerald-trust',
  themeMode: 'dark',
  lightTheme: { navy: '#065F46', cy: '#34D399', gold: '#D97706', bg: '#F3FAF7', card: '#FFFFFF', txt: '#0F172A' },
  darkTheme: { navy: '#6EE7B7', cy: '#34D399', gold: '#FBBF24', bg: '#07140F', card: '#0F241C', txt: '#E5E7EB' },
}

function Tenant({ slug }: { slug: string }) {
  useTenantTheme({ slug })
  return <ThemeSwitch />
}

function renderTheme(api: AppearanceApi) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <ThemeProvider api={api}>
        <Tenant slug="aurora" />
      </ThemeProvider>
    </QueryClientProvider>,
  )
}

const root = document.documentElement
const token = (name: string) => root.style.getPropertyValue(`--${name}`)

afterEach(() => {
  root.removeAttribute('style')
  delete root.dataset.theme
})

describe('ThemeProvider (RCU-PLT-006)', () => {
  it("applies the tenant's theme and default mode from the admin portal", async () => {
    renderTheme({ getTenantAppearance: () => Promise.resolve(emerald) })
    await waitFor(() => expect(root.dataset.theme).toBe('dark'))
    expect(token('navy')).toBe('#6EE7B7')
    expect(screen.getByRole('radio', { name: 'Dark' })).toBeChecked()
  })

  it("lets the user override the tenant's default and remembers the choice", async () => {
    renderTheme({ getTenantAppearance: () => Promise.resolve(emerald) })
    await waitFor(() => expect(token('navy')).toBe('#6EE7B7'))
    await userEvent.click(screen.getByRole('radio', { name: 'Light' }))
    expect(root.dataset.theme).toBe('light')
    expect(token('navy')).toBe('#065F46')
    expect(localStorage.getItem(THEME_MODE_STORAGE_KEY)).toBe('light')
  })

  it('keeps the built-in theme when the admin portal is unreachable', async () => {
    renderTheme({ getTenantAppearance: () => Promise.resolve(null) })
    await waitFor(() => expect(root.dataset.theme).toBe('light'))
    expect(token('navy')).toBe('')
    await userEvent.click(screen.getByRole('radio', { name: 'Dark' }))
    expect(root.dataset.theme).toBe('dark')
    expect(token('navy')).toBe('')
  })
})
