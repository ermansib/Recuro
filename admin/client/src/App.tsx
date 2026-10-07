import { Navigate, Route, Routes } from 'react-router-dom'
import type { AdminLevel } from './auth/personaStorage'
import { useSession } from './auth/sessionContext'
import { Layout } from './components/Layout'
import { BrandingPage } from './features/branding/BrandingPage'
import { ScreenEditorPage } from './features/screens/ScreenEditorPage'
import { ScreensPage } from './features/screens/ScreensPage'
import { SignInPage } from './features/signin/SignInPage'
import { TenantsPage } from './features/tenants/TenantsPage'
import { ThemeLibraryPage } from './features/themes/ThemeLibraryPage'

const HOME: Record<AdminLevel, string> = {
  platform: '/platform/tenants',
  tenant: '/tenant/branding',
}

export function App() {
  const { level } = useSession()

  if (!level) {
    return (
      <Routes>
        <Route path="*" element={<SignInPage />} />
      </Routes>
    )
  }

  // Each admin level only gets its own routes. The API enforces the same split; this is a convenience.
  return (
    <Routes>
      <Route element={<Layout />}>
        {level === 'platform' ? (
          <>
            <Route path="/platform/tenants" element={<TenantsPage />} />
            <Route path="/platform/themes" element={<ThemeLibraryPage />} />
          </>
        ) : (
          <>
            <Route path="/tenant/branding" element={<BrandingPage />} />
            <Route path="/tenant/screens" element={<ScreensPage />} />
            <Route path="/tenant/screens/:key" element={<ScreenEditorPage />} />
          </>
        )}
        <Route path="*" element={<Navigate to={HOME[level]} replace />} />
      </Route>
    </Routes>
  )
}
