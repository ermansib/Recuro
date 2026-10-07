import { useTranslation } from 'react-i18next'
import { NavLink, Outlet } from 'react-router-dom'
import { useSession } from '../auth/sessionContext'
import type { AdminLevel } from '../auth/personaStorage'

interface NavItem {
  to: string
  labelKey: string
}

const NAV: Record<AdminLevel, NavItem[]> = {
  platform: [
    { to: '/platform/tenants', labelKey: 'common.nav.tenants' },
    { to: '/platform/themes', labelKey: 'common.nav.themes' },
  ],
  tenant: [
    { to: '/tenant/branding', labelKey: 'common.nav.branding' },
    { to: '/tenant/screens', labelKey: 'common.nav.screens' },
  ],
}

export function Layout() {
  const { t } = useTranslation()
  const { persona, level, signOut } = useSession()
  if (!persona || !level) return null

  return (
    <div className="shell">
      <a className="skip-link" href="#main">
        {t('common.skipToContent')}
      </a>
      <aside className="sidebar">
        <div className="brand">
          {t('common.product')}
          <small>{t(`common.level.${level}`)}</small>
        </div>
        <nav aria-label={t('common.nav.label')}>
          <ul>
            {NAV[level].map((item) => (
              <li key={item.to}>
                <NavLink to={item.to}>{t(item.labelKey)}</NavLink>
              </li>
            ))}
          </ul>
        </nav>
        <div className="who">
          <span>
            <strong>{persona.tenantName ?? persona.role}</strong>
            {persona.tenantName && <> · {persona.role}</>}
          </span>
          <button type="button" className="btn secondary small" onClick={signOut}>
            {t('common.signOut')}
          </button>
        </div>
      </aside>
      <main id="main" className="main" tabIndex={-1}>
        <Outlet />
      </main>
    </div>
  )
}
