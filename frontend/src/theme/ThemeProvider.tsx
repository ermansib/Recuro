// Applies the tenant's theme from the admin portal and the user's light/dark choice (RCU-PLT-006).
import { useQuery } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { appearanceApi } from '../api/client'
import type { AppearanceApi } from '../api/contract'
import { applyTenantTheme } from '../config/tenant'
import type { ThemeMode } from '../domain/types'
import { ThemeContext, type ThemeSource, type ThemeValue } from './themeContext'
import { effectiveMode, resolveScheme, themeTokensFor } from './tokens'

export const THEME_MODE_STORAGE_KEY = 'recuro.themeMode'
const DARK_QUERY = '(prefers-color-scheme: dark)'
/** Re-read the tenant's theme at most this often, so a change published in the admin portal shows up. */
const APPEARANCE_STALE_MS = 60_000

function readStoredMode(): ThemeMode | null {
  try {
    const stored = localStorage.getItem(THEME_MODE_STORAGE_KEY)
    return stored === 'system' || stored === 'light' || stored === 'dark' ? stored : null
  } catch {
    return null
  }
}

function storeMode(mode: ThemeMode): void {
  try {
    localStorage.setItem(THEME_MODE_STORAGE_KEY, mode)
  } catch {
    // Private mode or storage disabled: the choice lasts for this page only.
  }
}

const darkQuery = (): MediaQueryList | null =>
  typeof window !== 'undefined' && typeof window.matchMedia === 'function' ? window.matchMedia(DARK_QUERY) : null

/** Tracks the device's light/dark preference. */
function useDevicePrefersDark(): boolean {
  const [prefersDark, setPrefersDark] = useState(() => darkQuery()?.matches ?? false)
  useEffect(() => {
    const query = darkQuery()
    if (!query) return
    const onChange = (e: MediaQueryListEvent) => setPrefersDark(e.matches)
    query.addEventListener('change', onChange)
    return () => query.removeEventListener('change', onChange)
  }, [])
  return prefersDark
}

export function ThemeProvider({ children, api = appearanceApi }: { children: ReactNode; api?: AppearanceApi }) {
  const [userChoice, setUserChoice] = useState<ThemeMode | null>(readStoredMode)
  const [source, setSourceState] = useState<ThemeSource | null>(null)
  const prefersDark = useDevicePrefersDark()

  const slug = source?.slug
  const appearance = useQuery({
    queryKey: ['appearance', slug],
    queryFn: () => api.getTenantAppearance(slug ?? ''),
    enabled: Boolean(slug),
    staleTime: APPEARANCE_STALE_MS,
    refetchOnWindowFocus: true,
  })
  const tenantAppearance = slug ? appearance.data : null

  const mode = effectiveMode(userChoice, tenantAppearance?.themeMode)
  const scheme = resolveScheme(mode, prefersDark)

  useEffect(() => {
    const brand = tenantAppearance
      ? scheme === 'dark'
        ? tenantAppearance.darkTheme
        : tenantAppearance.lightTheme
      : // The mock tenant theme is a light palette; in dark mode the built-in dark tokens apply.
        scheme === 'light'
        ? source?.fallback
        : undefined
    document.documentElement.dataset.theme = scheme
    applyTenantTheme(themeTokensFor(brand, scheme))
  }, [tenantAppearance, scheme, source?.fallback])

  const setMode = useCallback((next: ThemeMode) => {
    storeMode(next)
    setUserChoice(next)
  }, [])

  const setSource = useCallback((next: ThemeSource | null) => {
    setSourceState((current) =>
      current?.slug === next?.slug && current?.fallback === next?.fallback ? current : next,
    )
  }, [])

  const value = useMemo<ThemeValue>(() => ({ mode, scheme, setMode, setSource }), [mode, scheme, setMode, setSource])
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}
