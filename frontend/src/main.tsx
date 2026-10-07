import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { api } from './api/client'
import { App } from './App'
import { SessionProvider } from './auth/session'
import { ToastProvider } from './components/Toasts'
import { applyTenantTheme } from './config/tenant'
import './i18n'
import './styles/tokens.css'
import './styles/app.css'

const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } } })

applyTenantTheme()

const users = await api.getUsers()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <SessionProvider users={users}>
          <ToastProvider>
            <App />
          </ToastProvider>
        </SessionProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
)
