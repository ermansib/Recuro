import { fileURLToPath } from 'node:url'
import react from '@vitejs/plugin-react'
import { loadEnv, searchForWorkspaceRoot } from 'vite'
import { defineConfig } from 'vitest/config'

// Tenant themes come from the admin portal (admin/, `dotnet run` serves it on 5080). The dev server
// proxies its runtime endpoint so the browser needs no CORS. Set ADMIN_API_PROXY to point elsewhere.
const DEFAULT_ADMIN_API = 'http://127.0.0.1:5080'

// The create-workspace form is defined once, next to the admin API that owns it (admin/contracts).
const WORKSPACE_FORM = fileURLToPath(new URL('../admin/contracts/workspace-form', import.meta.url))

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  return {
    plugins: [react()],
    resolve: { alias: { '@workspace-form': WORKSPACE_FORM } },
    server: {
      fs: { allow: [searchForWorkspaceRoot(process.cwd()), WORKSPACE_FORM] },
      proxy: {
        '/api/v1/runtime': { target: env.ADMIN_API_PROXY || DEFAULT_ADMIN_API, changeOrigin: true },
        '/api/v1/workspaces': { target: env.ADMIN_API_PROXY || DEFAULT_ADMIN_API, changeOrigin: true },
        '/api/v1/me': { target: env.ADMIN_API_PROXY || DEFAULT_ADMIN_API, changeOrigin: true },
      },
    },
    test: {
      environment: 'jsdom',
      globals: true,
      setupFiles: ['./src/test/setup.ts'],
    },
  }
})
