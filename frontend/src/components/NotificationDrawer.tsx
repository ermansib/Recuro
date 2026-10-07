import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { useApiMutation, useEmails, useNotifications } from '../api/hooks'
import { useSession } from '../auth/sessionContext'
import type { EmailMessage } from '../domain/types'
import { relativeTime } from '../utils/format'
import { EmailModal } from './EmailModal'
import { useToast } from './toastContext'

export function NotificationDrawer({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation()
  const { user } = useSession()
  const navigate = useNavigate()
  const toast = useToast()
  const [tab, setTab] = useState<'notif' | 'email'>('notif')
  const [unreadOnly, setUnreadOnly] = useState(false)
  const [openEmail, setOpenEmail] = useState<EmailMessage | null>(null)
  const notifications = useNotifications()
  const emails = useEmails()
  const markNotif = useApiMutation(api.markNotificationRead)
  const markEmail = useApiMutation(api.markEmailRead)
  const markAll = useApiMutation(api.markAllRead)
  const simulate = useApiMutation(api.simulateEvent)

  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && !openEmail && onClose()
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [open, onClose, openEmail])

  const nUnread = notifications.data?.filter((n) => n.unread).length ?? 0
  const eUnread = emails.data?.filter((e) => e.unread).length ?? 0
  const notifItems = (notifications.data ?? []).filter((n) => !unreadOnly || n.unread)
  const emailItems = (emails.data ?? []).filter((e) => !unreadOnly || e.unread)

  return (
    <>
      <div className={`drawer-bk ${open ? 'open' : ''}`} onClick={onClose} />
      <aside className={`drawer ${open ? 'open' : ''}`} aria-hidden={!open} aria-label={t('common.drawer.title')} inert={!open}>
        <div className="dr-head">
          <h3>{t('common.drawer.title')}</h3>
          <button type="button" onClick={onClose} aria-label={t('common.close')}>
            ✕
          </button>
        </div>
        <div className="dr-tabs" role="tablist">
          <button type="button" role="tab" aria-selected={tab === 'notif'} className={`dr-tab ${tab === 'notif' ? 'on' : ''}`} onClick={() => setTab('notif')}>
            {t('common.drawer.notifications')} {nUnread > 0 && t('common.drawer.newCount', { count: nUnread })}
          </button>
          <button type="button" role="tab" aria-selected={tab === 'email'} className={`dr-tab ${tab === 'email' ? 'on' : ''}`} onClick={() => setTab('email')}>
            {t('common.drawer.emails')} {eUnread > 0 && t('common.drawer.newCount', { count: eUnread })}
          </button>
        </div>
        <div className="dr-tools">
          <button type="button" className={`fchip ${!unreadOnly ? 'on' : ''}`} onClick={() => setUnreadOnly(false)}>
            {t('common.drawer.all')}
          </button>
          <button type="button" className={`fchip ${unreadOnly ? 'on' : ''}`} onClick={() => setUnreadOnly(true)}>
            {t('common.drawer.unread')}
          </button>
          <button
            type="button"
            className="mra"
            onClick={() => markAll.mutate(user.role, { onSuccess: () => toast(t('common.drawer.markedAll')) })}
          >
            {t('common.drawer.markAll')}
          </button>
        </div>
        <div className="dr-list">
          {tab === 'notif' &&
            (notifItems.length === 0 ? (
              <div className="empty">
                <span className="eic">🔔</span>
                {t('common.drawer.emptyNotif')}
              </div>
            ) : (
              notifItems.map((n) => (
                <button
                  type="button"
                  key={n.id}
                  className={`n-item ${n.unread ? 'unread' : 'read'}`}
                  style={{ width: '100%', textAlign: 'left', background: undefined }}
                  onClick={() => {
                    markNotif.mutate(n.id)
                    if (n.link) {
                      onClose()
                      navigate(n.link)
                    }
                  }}
                >
                  <span className="n-ic">{n.icon}</span>
                  <span className="n-main">
                    <b>{n.title}</b>
                    <p>{n.body}</p>
                  </span>
                  <span className="n-side">
                    <span className="n-time">{relativeTime(n.createdAt)}</span>
                    <span className="n-dot" />
                  </span>
                </button>
              ))
            ))}
          {tab === 'email' &&
            (emailItems.length === 0 ? (
              <div className="empty">
                <span className="eic">✉</span>
                {t('common.drawer.emptyEmail')}
              </div>
            ) : (
              emailItems.map((e) => (
                <button
                  type="button"
                  key={e.id}
                  className={`n-item ${e.unread ? 'unread' : 'read'}`}
                  style={{ width: '100%', textAlign: 'left' }}
                  onClick={() => {
                    markEmail.mutate(e.id)
                    setOpenEmail(e)
                  }}
                >
                  <span className="n-ic">{e.unread ? '📬' : '📭'}</span>
                  <span className="n-main">
                    <b>{e.subject}</b>
                    <p>
                      <b style={{ display: 'inline' }}>{t('common.drawer.from')}:</b> {e.from}
                    </p>
                  </span>
                  <span className="n-side">
                    <span className="n-time">{relativeTime(e.createdAt)}</span>
                    <span className="n-dot" />
                  </span>
                </button>
              ))
            ))}
        </div>
        <div className="dr-foot">
          <span className="subnote" style={{ flex: 1 }}>
            {t('common.drawer.footNote')}
          </span>
          <button
            type="button"
            className="btn btn-primary btn-sm"
            onClick={() => simulate.mutate(user.role, { onSuccess: (msg) => toast(`⚡ ${msg}`, 'move') })}
          >
            {t('common.drawer.simulate')}
          </button>
        </div>
      </aside>
      {openEmail && <EmailModal email={openEmail} onClose={() => setOpenEmail(null)} />}
    </>
  )
}
