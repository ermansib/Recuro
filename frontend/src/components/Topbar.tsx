import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { NavLink, useNavigate } from 'react-router-dom'
import { useApprovals, useEmails, useNotifications } from '../api/hooks'
import { homePath } from '../auth/permissions'
import { useSession } from '../auth/sessionContext'
import type { Role } from '../domain/types'
import { Logo } from './Logo'
import { NAV_ITEMS } from './nav'
import { useToast } from './toastContext'

export function Topbar({ onOpenDrawer }: { onOpenDrawer: () => void }) {
  const { t } = useTranslation()
  const { user, users, switchRole } = useSession()
  const navigate = useNavigate()
  const toast = useToast()
  const [menuOpen, setMenuOpen] = useState(false)
  const menuRef = useRef<HTMLDivElement>(null)
  const approvals = useApprovals()
  const notifications = useNotifications()
  const emails = useEmails()

  const pending = approvals.data?.filter((a) => !a.decision).length ?? 0
  const unread =
    (notifications.data?.filter((n) => n.unread).length ?? 0) + (emails.data?.filter((e) => e.unread).length ?? 0)

  useEffect(() => {
    if (!menuOpen) return
    const close = (e: MouseEvent) => {
      if (!menuRef.current?.contains(e.target as Node)) setMenuOpen(false)
    }
    document.addEventListener('mousedown', close)
    return () => document.removeEventListener('mousedown', close)
  }, [menuOpen])

  const choose = (role: Role) => {
    switchRole(role)
    setMenuOpen(false)
    navigate(homePath(role))
    toast(t('common.persona.viewingAs', { role: t(`common.roles.${role}`) }) + (role === 'mdceo' ? t('common.persona.masked') : ''))
  }

  return (
    <header className="topbar">
      <NavLink to={homePath(user.role)} className="logo" style={{ textDecoration: 'none' }}>
        <Logo />
      </NavLink>
      <nav aria-label={t('common.nav.label')}>
        {NAV_ITEMS.filter((n) => n.roles.includes(user.role)).map((n) => (
          <NavLink key={n.to} to={n.to} end={n.to === '/'} className={({ isActive }) => `nav-tab ${isActive ? 'active' : ''}`}>
            {t(`common.nav.${n.key}`)}
            {n.key === 'approvals' && pending > 0 && <span className="tbadge">{pending}</span>}
          </NavLink>
        ))}
      </nav>
      <div className="top-right">
        <button type="button" className="bell" onClick={onOpenDrawer} aria-label={t('common.bell', { count: unread })}>
          🔔{unread > 0 && <span className="dot">{unread}</span>}
        </button>
        <div className="role-sw" ref={menuRef}>
          <button
            type="button"
            className="role-btn"
            aria-haspopup="menu"
            aria-expanded={menuOpen}
            aria-label={t('common.persona.button', { name: user.name })}
            onClick={() => setMenuOpen((o) => !o)}
          >
            <span className="rb-av">{user.initials}</span>
            <span>
              {user.name} · {t(`common.roles.${user.role}`)}
            </span>{' '}
            ▾
          </button>
          {menuOpen && (
            <div className="role-menu" role="menu">
              <div className="rm-title">{t('common.persona.title')}</div>
              {users.map((u) => (
                <button key={u.id} type="button" role="menuitem" className={`rm-item ${u.role === user.role ? 'active' : ''}`} onClick={() => choose(u.role)}>
                  <b>{u.name}</b>
                  <span>{u.title}</span>
                  <em>{u.summary}</em>
                </button>
              ))}
            </div>
          )}
        </div>
      </div>
    </header>
  )
}
