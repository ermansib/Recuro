import { fileURLToPath } from 'node:url'
import react from '@vitejs/plugin-react'
import { searchForWorkspaceRoot } from 'vite'
import { defineConfig } from 'vitest/config'

// The .NET API serves the built client from its wwwroot, so the admin portal ships as one deployable.
// In development Vite proxies /api to the API (dotnet run, http profile).
const API_URL = process.env.ADMIN_API_URL ?? 'http://localhost:5080'

// The create-workspace form shared with the main portal's sign-up page (admin/contracts).
const WORKSPACE_FORM = fileURLToPath(new URL('../contracts/workspace-form', import.meta.url))

export default defineConfig({
  plugins: [react()],
  resolve: { alias: { '@workspace-form': WORKSPACE_FORM } },
  server: {
    port: 5174,
    fs: { allow: [searchForWorkspaceRoot(process.cwd()), WORKSPACE_FORM] },
    proxy: { '/api': API_URL },
  },
  build: {
    outDir: '../server/src/Recuro.Admin.Api/wwwroot',
    emptyOutDir: true,
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
  },
})
