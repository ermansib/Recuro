/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Base URL of the admin portal API, e.g. https://admin.example.com. Empty means same origin. */
  readonly VITE_ADMIN_API_URL?: string
}
