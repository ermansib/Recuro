// White-label configuration (RCU-PLT-006). The product is Recuro; the tenant is the hiring
// company using it. Signed-in screens read the tenant from the session (useSession().tenant);
// `tenant` below is the demo tenant, used only where no session exists yet (e.g. formatting defaults).
import seed from '../mocks/data/tenant.json'
import type { TenantConfig } from '../domain/types'

export const tenant: TenantConfig = seed as TenantConfig

export const product = {
  name: 'Recuro',
  tagline: 'RECRUITMENT, UNINTERRUPTED',
}

let appliedTokens: string[] = []

/**
 * Applies tenant theme overrides on top of the default tokens in styles/tokens.css. Overrides
 * from a previously applied tenant are cleared first; pass nothing to restore the Recuro defaults.
 */
export function applyTenantTheme(theme: TenantConfig['theme'] = undefined): void {
  const root = document.documentElement
  for (const token of appliedTokens) root.style.removeProperty(`--${token}`)
  appliedTokens = Object.keys(theme ?? {})
  for (const [token, value] of Object.entries(theme ?? {})) {
    root.style.setProperty(`--${token}`, value)
  }
}
