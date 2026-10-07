import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { App } from './App'
import { httpApi } from './api/client'
import { SessionProvider } from './auth/SessionProvider'
import { developmentStrategy, strategyFor, type AuthStrategy } from './auth/strategies'
import './i18n'
import './styles/tokens.css'
import './styles/admin.css'

const queryClient = new QueryClient({ defaultOptions: { queries: { staleTime: 30_000, retry: 1 } } })

const root = document.getElementById('root')
if (!root) throw new Error('Root element #root is missing from index.html')

function render(strategy: AuthStrategy) {
  createRoot(root!).render(
    <StrictMode>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>
          <SessionProvider strategy={strategy}>
            <App />
          </SessionProvider>
        </BrowserRouter>
      </QueryClientProvider>
    </StrictMode>,
  )
}

// The API says how to sign in (Keycloak, or development personas). If it can't be reached, the
// sign-in page explains that rather than showing a blank screen.
httpApi
  .getAuthConfig()
  .then((config) => render(strategyFor(config)))
  .catch(() => render(developmentStrategy()))
