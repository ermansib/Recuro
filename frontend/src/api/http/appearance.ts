// HTTP client for the admin portal's runtime endpoint. Unlike the rest of the ApiClient this one is
// real today: the admin portal (admin/) already serves it. In development Vite proxies /api/v1/runtime
// to the admin API, so the base URL is empty; set VITE_ADMIN_API_URL to call another host.
import type { ThemeMode, ThemeTokens, TenantAppearance, WorkspaceRoleConfig } from '../../domain/types'
import type { AppearanceApi } from '../contract'

const THEME_MODES: readonly ThemeMode[] = ['system', 'light', 'dark']
const HEX_COLOR = /^#[0-9a-f]{6}$/i

/** Keeps only `token: #rrggbb` pairs, so nothing but a colour ever reaches a CSS custom property. */
function readTokens(value: unknown): ThemeTokens | null {
  if (typeof value !== 'object' || value === null) return null
  const tokens: ThemeTokens = {}
  for (const [key, colour] of Object.entries(value)) {
    if (/^[a-z][a-z-]*$/.test(key) && typeof colour === 'string' && HEX_COLOR.test(colour)) tokens[key] = colour
  }
  return Object.keys(tokens).length > 0 ? tokens : null
}

const KEY = /^[a-zA-Z][a-zA-Z0-9.-]*$/

const keyList = (value: unknown): string[] =>
  Array.isArray(value) ? value.filter((k): k is string => typeof k === 'string' && KEY.test(k)) : []

/** The workspace's roles, dropping anything that isn't a well-formed role. */
function readRoles(value: unknown): WorkspaceRoleConfig[] {
  if (!Array.isArray(value)) return []
  return value.flatMap((item: unknown) => {
    if (typeof item !== 'object' || item === null) return []
    const role = item as Record<string, unknown>
    if (typeof role.key !== 'string' || !KEY.test(role.key)) return []
    return [{
      key: role.key,
      name: typeof role.name === 'string' ? role.name : role.key,
      permissions: keyList(role.permissions),
      pages: keyList(role.pages),
    }]
  })
}

/** Maps the admin portal's RuntimeConfigDto onto TenantAppearance, or null if it isn't one. */
export function toTenantAppearance(body: unknown): TenantAppearance | null {
  if (typeof body !== 'object' || body === null) return null
  const dto = body as Record<string, unknown>
  const lightTheme = readTokens(dto.lightTheme)
  const darkTheme = readTokens(dto.darkTheme)
  if (typeof dto.slug !== 'string' || !lightTheme || !darkTheme) return null
  const themeMode = THEME_MODES.includes(dto.themeMode as ThemeMode) ? (dto.themeMode as ThemeMode) : 'system'
  const themePresetKey = typeof dto.themePresetKey === 'string' ? dto.themePresetKey : ''
  return { slug: dto.slug, themePresetKey, themeMode, lightTheme, darkTheme, roles: readRoles(dto.roles) }
}

export function createHttpAppearanceApi(baseUrl: string, fetcher: typeof fetch = (...args) => fetch(...args)): AppearanceApi {
  return {
    async getTenantAppearance(slug) {
      try {
        const res = await fetcher(`${baseUrl}/api/v1/runtime/${encodeURIComponent(slug)}`, {
          headers: { Accept: 'application/json' },
        })
        return res.ok ? toTenantAppearance(await res.json()) : null
      } catch {
        // Admin portal not running or not reachable: the built-in theme applies.
        return null
      }
    },
  }
}
