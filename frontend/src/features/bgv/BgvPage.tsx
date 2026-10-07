import { useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { api } from '../../api/client'
import { useApiMutation, useBgvCase, usePipeline } from '../../api/hooks'
import { can } from '../../auth/permissions'
import { useSession } from '../../auth/sessionContext'
import { useErrorToast, useToast } from '../../components/toastContext'
import { Card, Chip, ErrorBox, Loading, Modal, PageHead, Sensitive } from '../../components/ui'
import { summariseBgv } from '../../domain/rules'
import type { BgvCase, BgvCheckStatus, BgvCheckType, ChipTone } from '../../domain/types'
import { formatDate } from '../../utils/format'

const APP_ID = 'APP-2026-0390'
const STATUS_TONE: Record<BgvCheckStatus, ChipTone> = { Pending: 'slate', InProgress: 'amber', Cleared: 'green', Flagged: 'red', NotApplicable: 'slate' }

export function BgvPage() {
  const { t } = useTranslation()
  const { user, actor } = useSession()
  const toast = useToast()
  const onError = useErrorToast()
  const bgv = useBgvCase(APP_ID)
  const pipeline = usePipeline('REQ-2026-0156')
  const [reporting, setReporting] = useState(false)
  const release = useApiMutation((id: string) => api.releaseOfferAfterBgv(actor, id))

  if (bgv.isPending || pipeline.isPending) return <Loading />
  if (bgv.isError) return <ErrorBox error={bgv.error} />
  const card = pipeline.data?.find((c) => c.application.appId === APP_ID)
  const b = bgv.data
  const s = summariseBgv(b)
  const name = card?.candidate.name ?? ''

  return (
    <div className="screen">
      <PageHead title={t('bgv.title')} policy={t('bgv.policy')} crumb={t('bgv.crumb', { reqId: card?.application.reqId, name })}>
        <Link to="/pipeline" className="btn btn-ghost">{t('bgv.back')}</Link>
      </PageHead>
      {b.adverse && (
        <div className="hold-banner" role="alert">
          <Trans i18nKey="bgv.hold" components={{ b: <b /> }} />
        </div>
      )}
      <div className="req-strip">
        <span className="rid">{name}</span>
        <span className="sep" />
        <span><b>Sr Manager — Credit</b> · {card?.application.reqId} · {t('bgv.source', { source: card?.candidate.source })}</span>
        <span className="sep" />
        <Chip tone="navy">{t('bgv.vendor', { vendor: b.vendor })}</Chip>
        <Chip tone="slate">{t('bgv.case', { ref: b.vendorCaseRef })}</Chip>
        <Chip tone="gold">{t('bgv.initiated', { date: formatDate(b.initiatedAt) })}</Chip>
        <Chip tone={b.consentAt ? 'green' : 'red'}>{b.consentAt ? t('bgv.consent', { date: formatDate(b.consentAt) }) : t('bgv.noConsent')}</Chip>
        <div className="tat-bar" style={{ marginLeft: 'auto' }}>
          <div className="tat-track" role="progressbar" aria-valuemin={0} aria-valuemax={b.tatTotal} aria-valuenow={b.tatDay}>
            <i style={{ width: `${(b.tatDay / b.tatTotal) * 100}%` }} />
          </div>
          <Chip tone="teal">{t('bgv.day', { day: b.tatDay, total: b.tatTotal })}</Chip>
        </div>
      </div>
      <Card
        title={t('bgv.checksTitle')}
        sub={t('bgv.checksSub')}
        style={{ marginBottom: 16 }}
        aside={
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            <Chip tone="green">{t('bgv.counts.cleared', { count: s.cleared })}</Chip>
            <Chip tone="amber">{t('bgv.counts.inProgress', { count: s.inProgress })}</Chip>
            <Chip tone="slate">{t('bgv.counts.pending', { count: s.pending })}</Chip>
            <Chip tone="red">{t('bgv.counts.flagged', { count: s.flagged })}</Chip>
          </div>
        }
      >
        <div style={{ overflowX: 'auto' }}>
          <table className="mini">
            <thead>
              <tr>
                <th style={{ width: '26%' }}>{t('bgv.cols.check')}</th>
                <th>{t('bgv.cols.detail')}</th>
                <th style={{ width: '15%' }}>{t('bgv.cols.status')}</th>
                <th style={{ width: '24%' }}>{t('bgv.cols.note')}</th>
                <th style={{ width: '9%' }}>{t('bgv.cols.date')}</th>
              </tr>
            </thead>
            <tbody>
              {b.checks.map((c) => (
                <tr key={c.type}>
                  <td><b>{c.label}</b></td>
                  <td>{c.detail}</td>
                  <td><Chip tone={STATUS_TONE[c.status]}>{t(`bgv.status.${c.status}`)}</Chip></td>
                  <td>
                    {c.note}
                    {c.sensitiveNote && <> · <Sensitive mask="🔒">{c.sensitiveNote}</Sensitive></>}
                  </td>
                  <td>{c.date ? formatDate(c.date) : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>
      <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
        {can(user.role, 'bgv.reportAdverse') && (
          <button type="button" className="btn btn-danger" onClick={() => setReporting(true)}>{t('bgv.report')}</button>
        )}
        {can(user.role, 'offer.release') && (
          <button
            type="button"
            className="btn btn-primary"
            disabled={release.isPending}
            onClick={() => release.mutate(b.id, { onSuccess: () => toast(t('bgv.released'), 'move'), onError })}
          >
            {t('bgv.release')}
          </button>
        )}
        <button type="button" className="btn btn-ghost" onClick={() => toast(t('bgv.downloaded'))}>{t('bgv.download')}</button>
        <span style={{ marginLeft: 'auto', fontSize: 11, color: 'var(--muted)', alignSelf: 'center' }}>{t('bgv.adverseNote')}</span>
      </div>
      {reporting && <ReportModal bgv={b} onClose={() => setReporting(false)} />}
    </div>
  )
}

function ReportModal({ bgv, onClose }: { bgv: BgvCase; onClose: () => void }) {
  const { t } = useTranslation()
  const { actor } = useSession()
  const toast = useToast()
  const onError = useErrorToast()
  const open = bgv.checks.filter((c) => c.status === 'InProgress' || c.status === 'Pending')
  const [check, setCheck] = useState<BgvCheckType | ''>(open[0]?.type ?? '')
  const [description, setDescription] = useState('')
  const [action, setAction] = useState<'HoldAndEscalate' | 'SeekClarification'>('HoldAndEscalate')
  const report = useApiMutation(() => api.reportAdverseFinding(actor, bgv.id, { check: check as BgvCheckType, description, action }))
  const submit = () => {
    if (!description.trim() || !check) return toast(t('bgv.modal.required'), 'warn')
    report.mutate(undefined, {
      onSuccess: () => {
        toast(t('bgv.modal.done'), 'warn')
        onClose()
      },
      onError,
    })
  }
  return (
    <Modal
      title={t('bgv.modal.title')}
      onClose={onClose}
      footer={
        <>
          <button type="button" className="btn btn-ghost" onClick={onClose}>{t('common.cancel')}</button>
          <button type="button" className="btn btn-danger" disabled={report.isPending} onClick={submit}>{t('bgv.modal.submit')}</button>
        </>
      }
    >
      <div className="modal-b">
        <div className="field" style={{ marginBottom: 14 }}>
          <label htmlFor="disc-check">{t('bgv.modal.check')} <span className="req">*</span></label>
          <select id="disc-check" value={check} onChange={(e) => setCheck(e.target.value as BgvCheckType)}>
            {open.map((c) => <option key={c.type} value={c.type}>{c.label}</option>)}
          </select>
        </div>
        <div className="field" style={{ marginBottom: 14 }}>
          <label htmlFor="disc-desc">{t('bgv.modal.description')} <span className="req">*</span></label>
          <textarea id="disc-desc" rows={3} placeholder={t('bgv.modal.placeholder')} value={description} onChange={(e) => setDescription(e.target.value)} />
        </div>
        <div className="field" role="radiogroup" aria-label={t('bgv.modal.action')}>
          <label>{t('bgv.modal.action')}</label>
          <div className="pills">
            <input type="radio" name="disc-act" id="act-hold" checked={action === 'HoldAndEscalate'} onChange={() => setAction('HoldAndEscalate')} />
            <label htmlFor="act-hold">{t('bgv.modal.hold')}</label>
            <input type="radio" name="disc-act" id="act-clarify" checked={action === 'SeekClarification'} onChange={() => setAction('SeekClarification')} />
            <label htmlFor="act-clarify">{t('bgv.modal.clarify')}</label>
          </div>
        </div>
      </div>
    </Modal>
  )
}
