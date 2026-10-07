import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api } from '../../api/client'
import { useApiMutation, useApprovals } from '../../api/hooks'
import { useSession } from '../../auth/sessionContext'
import { useErrorToast, useToast } from '../../components/toastContext'
import { Card, Chip, ErrorBox, Loading, PageHead, Sensitive } from '../../components/ui'
import type { ApprovalItem } from '../../domain/types'
import { formatTime } from '../../utils/format'

export function ApprovalsPage() {
  const { t } = useTranslation()
  const { user } = useSession()
  const approvals = useApprovals()
  const pending = approvals.data?.filter((a) => !a.decision).length ?? 0
  const matrix = t('approvals.matrix.rows', { returnObjects: true }) as string[][]

  return (
    <div className="screen">
      <PageHead title={t('approvals.title', { role: t(`common.roles.${user.role}`) })} policy={t('approvals.policy')} crumb={t('approvals.crumb')}>
        <Chip tone="red">{t('approvals.pending', { count: pending })}</Chip>
      </PageHead>
      <div className="role-strip">
        <span className="rs-av">{user.initials}</span>
        <div>
          <b>{user.name} — {user.title}</b>
          <div className="rs-sub">{t(`approvals.subtitle.${user.role}`, { defaultValue: '' })}</div>
        </div>
        <Chip tone="slate" className="">{t('approvals.demo')}</Chip>
      </div>
      <div className="inbox-wrap">
        <div>
          {approvals.isPending && <Loading />}
          {approvals.isError && <ErrorBox error={approvals.error} />}
          {approvals.data?.length === 0 && <Card style={{ padding: 22 }}>{t('approvals.empty')}</Card>}
          {approvals.data?.map((item) => <ApprovalCard key={item.id} item={item} />)}
        </div>
        <div className="col">
          <Card title={t('approvals.sla.title')}>
            <div className="up-list">
              <div className="up-item"><div className="ui-main"><b>{t('approvals.sla.mrf')}</b><span>{t('approvals.sla.mrfSub')}</span></div><Chip tone="green">1.4d avg</Chip></div>
              <div className="up-item"><div className="ui-main"><b>{t('approvals.sla.offer')}</b><span>{t('approvals.sla.offerSub')}</span></div><Chip tone="green">1.1d avg</Chip></div>
              <div className="up-item"><div className="ui-main"><b>{t('approvals.sla.adverse')}</b><span>{t('approvals.sla.adverseSub')}</span></div><Chip tone="amber">2 open</Chip></div>
            </div>
          </Card>
          <Card title={t('approvals.matrix.title')} sub="§16">
            <table className="mini" style={{ fontSize: 11.5 }}>
              <thead><tr><th>{t('approvals.matrix.issue')}</th><th>{t('approvals.matrix.first')}</th><th>{t('approvals.matrix.final')}</th></tr></thead>
              <tbody>{matrix.map((r) => <tr key={r[0]}>{r.map((c) => <td key={c}>{c}</td>)}</tr>)}</tbody>
            </table>
          </Card>
          <div className="insight">{t('approvals.audit')}</div>
        </div>
      </div>
    </div>
  )
}

function ApprovalCard({ item }: { item: ApprovalItem }) {
  const { t } = useTranslation()
  const { actor } = useSession()
  const toast = useToast()
  const onError = useErrorToast()
  const [rejecting, setRejecting] = useState<string | null>(null)
  const [reason, setReason] = useState('')
  const decide = useApiMutation((a: { actionId: string; reason?: string }) => api.decideApproval(actor, item.id, a.actionId, a.reason))

  const act = (actionId: string, effect: string) => {
    if (effect === 'reject') return setRejecting(actionId)
    decide.mutate({ actionId }, {
      onSuccess: () => effect === 'query' && toast(t('approvals.querySent')),
      onError,
    })
  }
  const confirmReject = () => {
    if (!rejecting) return
    if (!reason.trim()) return toast(t('approvals.reasonRequired'), 'warn')
    decide.mutate({ actionId: rejecting, reason }, { onError })
  }

  const toneClass = item.tone === 'default' ? '' : item.tone
  return (
    <article className={`appr-card ${toneClass} ${item.isNew && !item.decision ? 'new-card' : ''} ${item.decision ? 'done' : ''}`}>
      <div className="ap-top">
        <b>
          {item.title}
          {item.isNew && !item.decision && <span className="new-tag">{t('approvals.new')}</span>}
        </b>
        <Chip tone={item.chip.tone}>{item.chip.text}</Chip>
      </div>
      <div className="ap-meta">
        {item.meta}
        {item.sensitive && (
          <>
            {' · '}
            {item.sensitive.label} <b><Sensitive>{item.sensitive.value}</Sensitive></b>
          </>
        )}
      </div>
      <div className="ap-route">{item.route}</div>
      <div className="ap-actions">
        {item.decision ? (
          <Chip tone="green">{t('approvals.decided', { text: item.decision.text, time: formatTime(item.decision.at) })}</Chip>
        ) : rejecting ? (
          <>
            <input className="reason-in" aria-label={t('approvals.reasonLabel')} placeholder={t('approvals.reasonPlaceholder')} value={reason} onChange={(e) => setReason(e.target.value)} autoFocus />
            <button type="button" className="btn btn-danger btn-sm" disabled={decide.isPending} onClick={confirmReject}>{t('approvals.confirm')}</button>
            <button type="button" className="btn btn-ghost btn-sm" onClick={() => setRejecting(null)}>{t('common.cancel')}</button>
          </>
        ) : (
          item.actions.map((a) => (
            <button key={a.id} type="button" className={`btn btn-${a.style} btn-sm`} disabled={decide.isPending} onClick={() => act(a.id, a.effect)}>
              {a.label}
            </button>
          ))
        )}
      </div>
    </article>
  )
}
