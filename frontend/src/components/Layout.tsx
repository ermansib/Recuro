import { useCallback, useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { Outlet } from 'react-router-dom'
import { useRules } from '../api/hooks'
import { tenant } from '../config/tenant'
import { NotificationDrawer } from './NotificationDrawer'
import { Topbar } from './Topbar'

export function Layout() {
  const { t } = useTranslation()
  const rules = useRules()
  const [drawerOpen, setDrawerOpen] = useState(false)
  const close = useCallback(() => setDrawerOpen(false), [])
  return (
    <>
      <a href="#main" className="sr-only">
        Skip to content
      </a>
      <Topbar onOpenDrawer={() => setDrawerOpen(true)} />
      <div className="mockup-note">
        <Trans i18nKey="common.banner" values={{ tenant: tenant.name }} components={{ b: <b /> }} />
      </div>
      <main id="main">
        <Outlet context={{ openDrawer: () => setDrawerOpen(true) }} />
      </main>
      <footer className="app">
        <span>{t('common.footer.left')}</span>
        <span>{t('common.footer.right', { tenant: tenant.name, version: rules.data?.version ?? '' })}</span>
      </footer>
      <NotificationDrawer open={drawerOpen} onClose={close} />
    </>
  )
}
