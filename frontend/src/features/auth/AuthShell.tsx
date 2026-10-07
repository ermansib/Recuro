import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Logo } from '../../components/Logo'
import { ThemeSwitch } from '../../components/ThemeSwitch'
import type { WorkspaceBranding } from '../../domain/types'
import { useTenantTheme } from '../../theme/themeContext'

/** Split layout for every signed-out page: tenant branding on one side, the form on the other. */
export function AuthShell({ branding, children }: { branding?: WorkspaceBranding; children: ReactNode }) {
  const { t } = useTranslation()

  useTenantTheme(branding ? { slug: branding.slug, fallback: branding.theme } : null)

  return (
    <div className="auth-shell">
      <aside className="auth-brand">
        {branding ? (
          <div>
            <div className="ph-tag">{t('auth.workspaceTag', { name: branding.name })}</div>
            <h1>{branding.careersTagline}</h1>
          </div>
        ) : (
          <div>
            <h1>{t('auth.brandPitch')}</h1>
            <ul>
              <li>{t('auth.brandPoints.one')}</li>
              <li>{t('auth.brandPoints.two')}</li>
              <li>{t('auth.brandPoints.three')}</li>
            </ul>
          </div>
        )}
        <div className="auth-powered">
          <div className="logo">
            <Logo />
          </div>
          {branding && <span>{t('auth.poweredBy')}</span>}
        </div>
      </aside>
      <main id="main" className="auth-main">
        <ThemeSwitch className="on-page auth-theme" />
        <div className="auth-card">{children}</div>
      </main>
    </div>
  )
}
