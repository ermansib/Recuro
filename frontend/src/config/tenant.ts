// White-label configuration (RCU-PLT-006). The product is Recuro; the tenant is the hiring
// company using it. Swap this for a per-tenant config fetched at sign-in when multi-tenancy lands.
import seed from '../mocks/data/tenant.json'
import type { TenantConfig } from '../domain/types'

export const tenant: TenantConfig = seed as TenantConfig

export const product = {
  name: 'Recuro',
  tagline: 'RECRUITMENT, UNINTERRUPTED',
}

/** Applies tenant theme overrides on top of the default tokens in styles/tokens.css. */
export function applyTenantTheme(config: TenantConfig = tenant): void {
  const root = document.documentElement
  for (const [token, value] of Object.entries(config.theme ?? {})) {
    root.style.setProperty(`--${token}`, value)
  }
}
