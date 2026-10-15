/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Base URL of the admin portal API, e.g. https://admin.example.com. Empty means same origin. */
  readonly VITE_ADMIN_API_URL?: string
  readonly VITE_EMPLOYEE_API_URL?: string
  /** OpenID Connect issuer the portal signs in with, e.g. https://id.example.com/realms/recuro. */
  readonly VITE_OIDC_AUTHORITY?: string
  /** Public OIDC client of the portal in that realm. */
  readonly VITE_OIDC_CLIENT_ID?: string
  /** Keeps the demo persona switcher in a production build. */
  readonly VITE_DEMO_PERSONAS?: string
}
