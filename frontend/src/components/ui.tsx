import { useEffect, useRef, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { can } from '../auth/permissions'
import { useSession } from '../auth/sessionContext'
import type { ChipTone } from '../domain/types'

export function Chip({ tone, children, className = '' }: { tone: ChipTone; children: ReactNode; className?: string }) {
  return <span className={`chip ${tone} ${className}`}>{children}</span>
}

export function PageHead({ title, policy, crumb, children }: { title: ReactNode; policy?: string; crumb?: ReactNode; children?: ReactNode }) {
  return (
    <div className="page-head">
      <div>
        <h1>
          {title} {policy && <span className="sopref">{policy}</span>}
        </h1>
        {crumb && <div className="crumb">{crumb}</div>}
      </div>
      {children && <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>{children}</div>}
    </div>
  )
}

export function Card({ title, sub, aside, children, className = '', style }: {
  title?: ReactNode
  sub?: ReactNode
  aside?: ReactNode
  children: ReactNode
  className?: string
  style?: React.CSSProperties
}) {
  return (
    <section className={`card ${className}`} style={style}>
      {title && (
        <div className="card-h">
          <h3>
            {title} {sub && <span className="sub">{sub}</span>}
          </h3>
          {aside}
        </div>
      )}
      {children}
    </section>
  )
}

/** CTC and contact PII: shown to HR roles, masked for others (RCU-PLT-003). */
export function Sensitive({ children, mask }: { children: ReactNode; mask?: ReactNode }) {
  const { user } = useSession()
  const { t } = useTranslation()
  return can(user.role, 'candidate.viewSensitive') ? <>{children}</> : <span title={t('common.masked')}>{mask ?? t('common.masked')}</span>
}

export function Loading() {
  const { t } = useTranslation()
  return <div className="loading">{t('common.loading')}</div>
}

export function ErrorBox({ error }: { error: unknown }) {
  const { t } = useTranslation()
  return <div className="loading err-text">{t('common.error', { message: error instanceof Error ? error.message : String(error) })}</div>
}

export function Modal({ title, tone = 'red', onClose, children, footer, className = '' }: {
  title: ReactNode
  tone?: 'red' | 'navy'
  onClose: () => void
  children: ReactNode
  footer: ReactNode
  className?: string
}) {
  const { t } = useTranslation()
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const prev = document.activeElement as HTMLElement | null
    ref.current?.querySelector<HTMLElement>('input,select,textarea,button')?.focus()
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('keydown', onKey)
      prev?.focus()
    }
  }, [onClose])
  return (
    <div className="modal-bk" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className={`modal ${className}`} role="dialog" aria-modal="true" ref={ref}>
        <div className={`modal-h ${tone === 'navy' ? 'navy' : ''}`}>
          <h3>{title}</h3>
          <button type="button" onClick={onClose} aria-label={t('common.close')}>
            ✕
          </button>
        </div>
        {children}
        <div className="modal-f">{footer}</div>
      </div>
    </div>
  )
}

/** Small sparkline used by the KPI list. Values are SVG y-coordinates (0 = top). */
export function Sparkline({ points, color }: { points: number[]; color: string }) {
  const step = 78 / Math.max(points.length - 1, 1)
  return (
    <svg width="78" height="24" viewBox="0 0 78 24" aria-hidden="true">
      <polyline points={points.map((y, i) => `${i * step},${y}`).join(' ')} fill="none" style={{ stroke: color }} strokeWidth="2" strokeLinecap="round" />
    </svg>
  )
}
