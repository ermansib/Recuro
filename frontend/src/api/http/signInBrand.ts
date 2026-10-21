// Carries the workspace's look from the portal to the identity provider's password page, so that page
// (admin/keycloak/themes/recuro) renders as the next step of the same card: same colours, same light or
// dark mode, same workspace name. It travels as one query parameter on the authorization request; the
// theme validates every value before using it, and nothing in it is secret.

/** Query parameter the Recuro Keycloak theme reads. Keycloak ignores parameters it doesn't know. */
export const SIGN_IN_BRAND_PARAM = 'recuro_brand'

/** Colour tokens the password page uses. Everything else on it follows from these. */
export const SIGN_IN_BRAND_TOKENS = [
  'navy', 'navy-dk', 'navy-lt', 'navy-tint', 'on-navy',
  'gold', 'gold-lt', 'accent-line', 'accent-tx',
  'bar', 'bar-dk', 'bar-lt', 'on-bar', 'on-bar-muted',
  'red', 'red-lt', 'red-tx', 'green', 'green-lt', 'amber-lt', 'amber-tx',
  'bg', 'card', 'line', 'surface-2', 'surface-3', 'txt', 'txt-2', 'muted', 'faint',
] as const

/** A colour the theme will accept: hex or rgb()/rgba(). Anything else is dropped on both sides. */
const COLOUR = /^(#[0-9a-f]{3,8}|rgba?\([\d\s.,%]+\))$/i

export interface SignInBrand {
  v: 1
  /** Workspace address, so the page can link back to the portal's sign-in for it. */
  slug: string
  name: string
  tagline: string
  scheme: 'light' | 'dark'
  tokens: Partial<Record<(typeof SIGN_IN_BRAND_TOKENS)[number], string>>
}

/** Reads the colours the portal is showing right now, workspace theme and light/dark choice included. */
export function captureSignInLook(root: HTMLElement = document.documentElement): Pick<SignInBrand, 'scheme' | 'tokens'> {
  const style = getComputedStyle(root)
  const tokens: SignInBrand['tokens'] = {}
  for (const token of SIGN_IN_BRAND_TOKENS) {
    const value = style.getPropertyValue(`--${token}`).trim()
    if (COLOUR.test(value)) tokens[token] = value
  }
  return { scheme: root.dataset.theme === 'dark' ? 'dark' : 'light', tokens }
}

const toBase64Url = (bytes: Uint8Array) =>
  btoa(String.fromCharCode(...bytes)).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, '')

export function encodeSignInBrand(brand: SignInBrand): string {
  return toBase64Url(new TextEncoder().encode(JSON.stringify(brand)))
}

/** The inverse of `encodeSignInBrand`, with the same checks the Keycloak theme makes. `null` if unreadable. */
export function decodeSignInBrand(encoded: string): SignInBrand | null {
  try {
    const base64 = encoded.replaceAll('-', '+').replaceAll('_', '/')
    const json = new TextDecoder().decode(Uint8Array.from(atob(base64), (c) => c.charCodeAt(0)))
    const raw: unknown = JSON.parse(json)
    if (typeof raw !== 'object' || raw === null) return null
    const value = raw as Record<string, unknown>
    if (value.v !== 1 || typeof value.slug !== 'string' || typeof value.name !== 'string') return null
    const tokens: SignInBrand['tokens'] = {}
    const given = typeof value.tokens === 'object' && value.tokens !== null ? (value.tokens as Record<string, unknown>) : {}
    for (const token of SIGN_IN_BRAND_TOKENS) {
      const colour = given[token]
      if (typeof colour === 'string' && COLOUR.test(colour)) tokens[token] = colour
    }
    return {
      v: 1,
      slug: value.slug,
      name: value.name,
      tagline: typeof value.tagline === 'string' ? value.tagline : '',
      scheme: value.scheme === 'dark' ? 'dark' : 'light',
      tokens,
    }
  } catch {
    return null
  }
}
