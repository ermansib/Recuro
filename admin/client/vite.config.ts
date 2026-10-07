import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// The .NET API serves the built client from its wwwroot, so the admin portal ships as one deployable.
// In development Vite proxies /api to the API (dotnet run, http profile).
const API_URL = process.env.ADMIN_API_URL ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5174,
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
