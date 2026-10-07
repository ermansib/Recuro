import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router-dom'
import { useApprovals, useDashboard } from '../../api/hooks'
import { can } from '../../auth/permissions'
import { useSession } from '../../auth/sessionContext'
import { Card, Chip, ErrorBox, Loading, PageHead, Sparkline } from '../../components/ui'

const toneColor = { green: 'var(--green)', amber: 'var(--amber)', slate: 'var(--slate)' } as Record<string, string>

export function DashboardPage() {
  const { t } = useTranslation()
  const { user } = useSession()
  const navigate = useNavigate()
  const dashboard = useDashboard()
  const approvals = useApprovals()

  if (dashboard.isPending) return <Loading />
  if (dashboard.isError) return <ErrorBox error={dashboard.error} />
  const d = dashboard.data
  const pending = approvals.data?.filter((a) => !a.decision).length ?? 0
  const maxPipe = Math.max(...d.pipeline.map((p) => p.count), 1)
  const total = d.pipeline.reduce((a, p) => a + p.count, 0)
  const ok = d.kpis.filter((k) => k.tone === 'green').length
  const canRaise = can(user.role, 'mrf.raise')

  const quick = [
    canRaise && { to: '/mrf/new', ic: '＋', label: t('dashboard.quick.raiseMrf') },
    { to: '/jd', ic: '📝', label: t('dashboard.quick.jd') },
    can(user.role, 'mrf.approve') && { to: '/approvals', ic: '✅', label: t('dashboard.quick.approvals', { count: pending }) },
    { to: '/pipeline', ic: '👥', label: t('dashboard.quick.pipeline') },
    { to: '/offer', ic: '✉', label: t('dashboard.quick.offer') },
    { to: '/bgv', ic: '🛡', label: t('dashboard.quick.bgv') },
    { to: '/careers', ic: '🌐', label: t('dashboard.quick.careers') },
  ].filter(Boolean) as { to: string; ic: string; label: string }[]

  return (
    <div className="screen">
      <PageHead title={t('dashboard.title')} policy={t('dashboard.policy')} crumb={t('dashboard.crumb', { date: d.dateLabel })}>
        {canRaise && (
          <button type="button" className="btn btn-gold" onClick={() => navigate('/mrf/new')}>
            {t('dashboard.newMrf')}
          </button>
        )}
      </PageHead>
      <div className="stat-row">
        {d.stats.map((s) => (
          <div key={s.label} className={`stat ${s.tone}`}>
            <div className="lbl">{s.label}</div>
            <div className="val">{s.value}</div>
            <div className="trd">{s.trend}</div>
          </div>
        ))}
      </div>
      <div className="qa-row">
        {quick.map((q) => (
          <Link key={q.to} to={q.to} className="qa" style={{ textDecoration: 'none' }}>
            <span className="ic" aria-hidden="true">{q.ic}</span>
            {q.label}
          </Link>
        ))}
      </div>
      <div className="dash-grid">
        <div className="col">
          <Card title={t('dashboard.tat.title')} sub={t('dashboard.tat.sub')} aside={<Chip tone="red">{t('dashboard.tat.active', { count: d.tatBreaches.length })}</Chip>}>
            <div style={{ overflowX: 'auto' }}>
              <table className="mini">
                <thead>
                  <tr>
                    <th>{t('dashboard.tat.requisition')}</th>
                    <th>{t('dashboard.tat.stage')}</th>
                    <th>{t('dashboard.tat.escalation')}</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {d.tatBreaches.map((b) => (
                    <tr key={b.reqId}>
                      <td>
                        <b>{b.reqId}</b>
                        <br />
                        <span style={{ color: 'var(--muted)' }}>{b.position}</span>
                      </td>
                      <td><Chip tone={b.stageTone}>{b.stage}</Chip></td>
                      <td><Chip tone="slate">{b.escalation}</Chip></td>
                      <td>
                        <Link to={b.link} className="btn btn-ghost btn-sm">{t('dashboard.tat.open')}</Link>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Card>
          <Card title={t('dashboard.pipeline.title')} sub={t('dashboard.pipeline.sub')} aside={<Chip tone="navy">{t('dashboard.pipeline.total', { count: total })}</Chip>}>
            <div className="bars">
              {d.pipeline.map((p) => (
                <div key={p.stage} className="bar-row">
                  <span className="bl">{p.stage}</span>
                  <div className="bar-track" role="presentation">
                    <div className="bar-fill" style={{ width: `${(p.count / maxPipe) * 100}%`, background: p.color }} />
                  </div>
                  <b>{p.count}</b>
                </div>
              ))}
            </div>
          </Card>
        </div>
        <div className="col">
          <Card title={t('dashboard.kpis.title', { period: d.kpiPeriodLabel })} sub={t('dashboard.kpis.sub')} aside={<Chip tone="green">{t('dashboard.kpis.onTrack', { ok, total: d.kpis.length })}</Chip>}>
            <div className="kpi-list">
              {d.kpis.map((k) => (
                <div key={k.name} className="kpi">
                  <div className="k-name">
                    <b>{k.name}</b>
                    <span>{k.target}</span>
                  </div>
                  <Sparkline points={k.trend} color={toneColor[k.tone] ?? 'var(--slate)'} />
                  <span className="k-val">{k.value}</span>
                  <Chip tone={k.tone}>{k.status}</Chip>
                </div>
              ))}
            </div>
          </Card>
          <Card title={t('dashboard.upcoming.title')} sub={t('dashboard.upcoming.sub')}>
            <div className="up-list">
              {d.upcoming.map((u) => (
                <div key={u.title} className="up-item">
                  <div className="up-when">
                    <b>{u.day}</b>
                    <span>{u.month}</span>
                  </div>
                  <div className="ui-main">
                    <b>{u.title}</b>
                    <span>{u.detail}</span>
                  </div>
                  <Link to={u.link} className="btn btn-ghost btn-sm">{t('dashboard.upcoming.open')}</Link>
                </div>
              ))}
            </div>
          </Card>
        </div>
      </div>
    </div>
  )
}
