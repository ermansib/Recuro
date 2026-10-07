import { createContext, useContext, useEffect } from 'react'
import type { ColorScheme, ThemeMode, ThemeTokens } from '../domain/types'

/**
 * Whose theme to show. `slug` is looked up in the admin portal; `fallback` (the tenant's mock theme)
 * applies in light mode while the admin portal is unreachable. `null` means the plain Recuro theme.
 */
export interface ThemeSource {
  slug: string
  fallback?: ThemeTokens
}

export interface ThemeValue {
  /** The mode in effect: the user's choice, else the tenant's default, else `system`. */
  mode: ThemeMode
  /** Light or dark, after resolving `system` against the device setting. */
  scheme: ColorScheme
  setMode: (mode: ThemeMode) => void
  setSource: (source: ThemeSource | null) => void
}

export const ThemeContext = createContext<ThemeValue | null>(null)

export function useTheme(): ThemeValue {
  const value = useContext(ThemeContext)
  if (!value) throw new Error('useTheme must be used inside <ThemeProvider>')
  return value
}

/**
 * Shows `source`'s theme while the calling component is mounted. Pass `undefined` to leave the
 * current theme alone (e.g. while signed out, where the sign-in page decides).
 */
export function useTenantTheme(source: ThemeSource | null | undefined): void {
  const { setSource } = useTheme()
  const slug = source?.slug
  const fallback = source?.fallback
  const reset = source === null
  useEffect(() => {
    if (slug) setSource({ slug, fallback })
    else if (reset) setSource(null)
  }, [slug, fallback, reset, setSource])
}
