import { useMemo, useState, type DragEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router-dom'
import { api, type LogCandidateInput } from '../../api/client'
import { useApiMutation, usePipeline, useRequisitions, useRules } from '../../api/hooks'
import { can } from '../../auth/permissions'
import { useSession } from '../../auth/sessionContext'
import { useErrorToast, useToast } from '../../components/toastContext'
import { Chip, ErrorBox, Loading, Modal, PageHead, Sensitive } from '../../components/ui'
import { applicationTransitions, pipelineColumns } from '../../domain/stateMachines'
import type { ApplicationStage, CandidateSource, PipelineCard, Requisition } from '../../domain/types'
import { formatDate, initials } from '../../utils/format'
import { boardColumn } from './board'

const STAGE_COLOR: Record<string, string> = {
  Sourced: 'var(--slate)',
  Screened: 'var(--navy-lt)',
  Interview: 'var(--amber)',
  Selection: 'var(--purple)',
  BGV: 'var(--teal)',
  Offer: 'var(--green)',
}

const SOURCE_STYLE: Record<string, { background: string; color: string }> = {
  Portal: { background: 'var(--navy-tint)', color: 'var(--navy-lt)' },
  IJP: { background: 'var(--purple-lt)', color: 'var(--purple)' },
  Referral: { background: 'var(--gold-lt)', color: 'var(--accent-tx)' },
  'Walk-in': { background: 'var(--amber-lt)', color: 'var(--amber-tx)' },
  LinkedIn: { background: '#0A66C2', color: '#fff' },
  Consultant: { background: 'var(--surface-3)', color: 'var(--txt-2)' },
}

const SOURCING_OPEN = ['Approved', 'Sourcing', 'Interviewing', 'Selection', 'BGV', 'Offer']

export function PipelinePage() {
  const requisitions = useRequisitions()
  const [params, setParams] = useSearchParams()
  if (requisitions.isPending) return <Loading />
  if (requisitions.isError) return <ErrorBox error={requisitions.error} />
  const reqId = params.get('req') ?? 'REQ-2026-0156'
  const req = requisitions.data.find((r) => r.reqId === reqId) ?? requisitions.data[0]
  if (!req) return <ErrorBox error={new Error('No requisitions')} />
  return <Board req={req} all={requisitions.data} onSelect={(id) => setParams({ req: id })} />
}

function Board({ req, all, onSelect }: { req: Requisition; all: Requisition[]; onSelect: (id: string) => void }) {
  const { t } = useTranslation()
  const { user, actor } = useSession()
  const toast = useToast()
  const onError = useErrorToast()
  const pipeline = usePipeline(req.reqId)
  const [dragged, setDragged] = useState<string | null>(null)
  const [over, setOver] = useState<ApplicationStage | null>(null)
  const [rejecting, setRejecting] = useState<PipelineCard | null>(null)
  const [logging, setLogging] = useState(false)
  const canMove = can(user.role, 'pipeline.move')
  const canLog = can(user.role, 'candidate.log')
  const sourcingOpen = SOURCING_OPEN.includes(req.state)

  const move = useApiMutation((a: { appId: string; to: ApplicationStage }) => api.moveApplication(actor, a.appId, a.to))

  const columns = useMemo(() => {
    const map = new Map<ApplicationStage, PipelineCard[]>(pipelineColumns.map((s) => [s, []]))
    for (const card of pipeline.data ?? []) map.get(boardColumn(card))?.push(card)
    return map
  }, [pipeline.data])

  const sourceMix = useMemo(() => {
    const counts = new Map<string, number>()
    for (const c of pipeline.data ?? []) counts.set(c.candidate.source, (counts.get(c.candidate.source) ?? 0) + 1)
    return [...counts].map(([s, n]) => `${n} ${s}`).join(' · ')
  }, [pipeline.data])

  const doMove = (card: PipelineCard, to: ApplicationStage) => {
    if (to === 'Rejected') return setRejecting(card)
    move.mutate(
      { appId: card.application.appId, to },
      { onSuccess: () => toast(t('pipeline.moved', { name: card.candidate.name, stage: t(`pipeline.stages.${to}`) }), 'move'), onError },
    )
  }

  const onDrop = (e: DragEvent, to: ApplicationStage) => {
    e.preventDefault()
    setOver(null)
    const card = pipeline.data?.find((c) => c.application.appId === dragged)
    setDragged(null)
    if (!card || card.application.stage === to) return
    if (!canMove) return toast(t('pipeline.readOnly'), 'warn')
    doMove(card, to)
  }

  return (
    <div className="screen">
      <PageHead title={t('pipeline.title')} policy={t('pipeline.policy')} crumb={t('pipeline.crumb', { reqId: req.reqId })}>
        {!canMove && <Chip tone="slate">{t('pipeline.readOnly')}</Chip>}
        <label className="sr-only" htmlFor="req-select">{t('pipeline.requisition')}</label>
        <select id="req-select" className="btn btn-ghost" value={req.reqId} onChange={(e) => onSelect(e.target.value)}>
          {all.map((r) => (
            <option key={r.reqId} value={r.reqId}>
              {r.reqId} · {r.designation} ({t(`pipeline.states.${r.state}`)})
            </option>
          ))}
        </select>
        {canLog && (
          <button type="button" className="btn btn-primary" onClick={() => setLogging(true)}>
            {t('pipeline.log')}
          </button>
        )}
      </PageHead>
      <div className="req-strip">
        <span className="rid">{req.reqId}</span>
        <span className="sep" />
        <span>
          <b>{req.designation}</b> · Grade {req.grade} · {req.location}
        </span>
        <span className="sep" />
        <Chip tone="navy">{t('pipeline.band', { band: req.band })}</Chip>
        <Chip tone="gold">{t('pipeline.approver', { approver: req.route.approving })}</Chip>
        {sourcingOpen ? (
          <Chip tone="amber">{t('pipeline.age', { age: req.ageDays, tat: '25–35d' })}</Chip>
        ) : (
          <Chip tone="red">{t('pipeline.locked', { state: t(`pipeline.states.${req.state}`) })}</Chip>
        )}
        <span style={{ marginLeft: 'auto', fontSize: 12, color: 'var(--muted)' }}>{t('pipeline.dragHint')}</span>
      </div>
      {pipeline.isPending && <Loading />}
      {pipeline.isError && <ErrorBox error={pipeline.error} />}
      <div className="kanban">
        {pipelineColumns.map((stage) => {
          const cards = columns.get(stage) ?? []
          return (
            <section
              key={stage}
              className="kcol"
              style={{ ['--stage' as string]: STAGE_COLOR[stage] }}
              aria-label={t('pipeline.column', { stage: t(`pipeline.stages.${stage}`), count: cards.length })}
            >
              <div className="kcol-h">
                <span className="kt">{t(`pipeline.stages.${stage}`)}</span>
                <span className="kcount">{cards.length}</span>
              </div>
              <div
                className={`kcol-body ${over === stage ? 'over' : ''}`}
                onDragOver={(e) => {
                  e.preventDefault()
                  setOver(stage)
                }}
                onDragLeave={() => setOver(null)}
                onDrop={(e) => onDrop(e, stage)}
              >
                {cards.length === 0 && <span className="more-pill">{t('pipeline.empty')}</span>}
                {cards.map((card) => (
                  <KanbanCard
                    key={card.application.appId}
                    card={card}
                    canMove={canMove}
                    dragging={dragged === card.application.appId}
                    onDragStart={() => setDragged(card.application.appId)}
                    onDragEnd={() => setDragged(null)}
                    onMove={(to) => doMove(card, to)}
                  />
                ))}
              </div>
            </section>
          )
        })}
      </div>
      <div className="kanban-foot">
        <span>
          <b>{t('pipeline.foot.mix')}</b>&nbsp; {sourceMix}
        </span>
        <button type="button" className="btn btn-ghost btn-sm" style={{ marginLeft: 'auto' }} onClick={() => toast(t('pipeline.foot.summaryToast'))}>
          {t('pipeline.foot.summary')}
        </button>
      </div>
      {rejecting && <RejectModal card={rejecting} onClose={() => setRejecting(null)} />}
      {logging && <LogCandidateModal req={req} onClose={() => setLogging(false)} />}
    </div>
  )
}

function KanbanCard({ card, canMove, dragging, onDragStart, onDragEnd, onMove }: {
  card: PipelineCard
  canMove: boolean
  dragging: boolean
  onDragStart: () => void
  onDragEnd: () => void
  onMove: (to: ApplicationStage) => void
}) {
  const { t } = useTranslation()
  const { application: app, candidate: c } = card
  const next = applicationTransitions[app.stage].filter((s) => s !== 'Withdrawn')
  // From Hold, "forward" means back to the stage the candidate was held in.
  const forward = app.stage === 'Hold' ? boardColumn(card) : next.find((s) => pipelineColumns.includes(s) || s === 'PreBoarding')
  const closed = app.stage === 'Rejected' || app.stage === 'Withdrawn'
  const sourceStyle = SOURCE_STYLE[c.source] ?? SOURCE_STYLE.Consultant

  return (
    <article
      className={`kcard ${dragging ? 'hide' : ''} ${closed ? 'rejected' : ''} ${app.stage === 'Offer' ? 'offer-card' : ''}`}
      draggable={canMove && !closed}
      onDragStart={onDragStart}
      onDragEnd={onDragEnd}
      style={canMove ? undefined : { cursor: 'default' }}
    >
      <div className="k-top">
        <span className="kav" aria-hidden="true">{initials(c.name)}</span>
        <div>
          <div className="k-name">{c.name}</div>
          <div className="k-sub">{c.summary}</div>
        </div>
        {app.stage === 'Rejected' ? (
          <Chip tone="red">{t('pipeline.rejected')}</Chip>
        ) : app.stage === 'Hold' ? (
          <Chip tone="amber">{t('pipeline.hold')}</Chip>
        ) : (
          <span className="src-badge" style={sourceStyle}>{c.source}</span>
        )}
      </div>
      <div className="k-meta">
        {c.currentCtc !== null && (
          <>
            {t('pipeline.ctc')} <b><Sensitive mask="₹ •• L 🔒">₹{c.currentCtc}L</Sensitive></b>
            {c.expectedCtc !== null && (
              <>
                {' · '}
                {t('pipeline.expected')} <b><Sensitive mask="🔒">₹{c.expectedCtc}L</Sensitive></b>
              </>
            )}
            {c.noticeDays !== null && (
              <>
                {' · '}
                {t('pipeline.notice')} <b>{t('pipeline.noticeDays', { days: c.noticeDays })}</b>
              </>
            )}
            <br />
          </>
        )}
        {c.sourceRef && <>Ref: <b>{c.sourceRef}</b><br /></>}
        {app.note}
      </div>
      {!closed && (
        <div className="k-actions">
          {app.stage === 'Interview' && <Link to="/assessment" className="btn-like">{t('pipeline.feedback')}</Link>}
          {app.stage === 'BGV' && <Link to="/bgv" className="btn-like">{t('pipeline.viewChecks')}</Link>}
          {(app.stage === 'Selection' || app.stage === 'Offer') && <Link to="/offer" className="btn-like">{t('pipeline.offerDesk')}</Link>}
          {canMove && forward && (
            <button type="button" onClick={() => onMove(forward)}>
              {t('pipeline.advance', { stage: t(`pipeline.stages.${forward}`) })}
            </button>
          )}
          {canMove && next.includes('Rejected') && (
            <button type="button" className="warn" onClick={() => onMove('Rejected')}>
              {t('pipeline.reject')}
            </button>
          )}
        </div>
      )}
      {canMove && !closed && next.length > 0 && (
        <select
          className="k-move"
          aria-label={t('pipeline.moveLabel', { name: c.name })}
          value=""
          onChange={(e) => e.target.value && onMove(e.target.value as ApplicationStage)}
        >
          <option value="">{t('pipeline.moveTo')}</option>
          {next.map((s) => (
            <option key={s} value={s}>{t(`pipeline.stages.${s}`)}</option>
          ))}
        </select>
      )}
    </article>
  )
}

function RejectModal({ card, onClose }: { card: PipelineCard; onClose: () => void }) {
  const { t } = useTranslation()
  const { actor } = useSession()
  const toast = useToast()
  const onError = useErrorToast()
  const [reason, setReason] = useState('')
  const reject = useApiMutation((r: string) => api.rejectApplication(actor, card.application.appId, r))
  const confirm = () => {
    if (!reason.trim()) return toast(t('pipeline.rejectModal.required'), 'warn')
    reject.mutate(reason, {
      onSuccess: (app) => {
        toast(t('pipeline.rejectModal.done', { name: card.candidate.name, until: formatDate(app.rejection?.retainUntil ?? '') }), 'move')
        onClose()
      },
      onError,
    })
  }
  return (
    <Modal
      title={t('pipeline.rejectModal.title', { name: card.candidate.name })}
      onClose={onClose}
      footer={
        <>
          <button type="button" className="btn btn-ghost" onClick={onClose}>{t('common.cancel')}</button>
          <button type="button" className="btn btn-danger" disabled={reject.isPending} onClick={confirm}>{t('pipeline.rejectModal.confirm')}</button>
        </>
      }
    >
      <div className="modal-b">
        <div className="field" style={{ marginBottom: 12 }}>
          <label htmlFor="rej-reason">{t('pipeline.rejectModal.reason')} <span className="req">*</span></label>
          <textarea id="rej-reason" rows={3} placeholder={t('pipeline.rejectModal.placeholder')} value={reason} onChange={(e) => setReason(e.target.value)} />
        </div>
        <p className="subnote">{t('pipeline.rejectModal.note')}</p>
      </div>
    </Modal>
  )
}

function LogCandidateModal({ req, onClose }: { req: Requisition; onClose: () => void }) {
  const { t } = useTranslation()
  const { actor } = useSession()
  const rules = useRules()
  const toast = useToast()
  const onError = useErrorToast()
  const [v, setV] = useState<LogCandidateInput>({ reqId: req.reqId, name: '', email: '', phone: '', experienceYears: 0, source: 'Portal', privacyConsent: false })
  const log = useApiMutation((input: LogCandidateInput) => api.logCandidate(actor, input))
  const set = <K extends keyof LogCandidateInput>(k: K, value: LogCandidateInput[K]) => setV((p) => ({ ...p, [k]: value }))
  const submit = () =>
    log.mutate(v, {
      onSuccess: (card) => {
        toast(t('pipeline.logModal.done', { name: card.candidate.name, appId: card.application.appId, source: card.candidate.source }), 'move')
        onClose()
      },
      onError,
    })
  return (
    <Modal
      title={t('pipeline.logModal.title', { reqId: req.reqId })}
      tone="navy"
      onClose={onClose}
      footer={
        <>
          <button type="button" className="btn btn-ghost" onClick={onClose}>{t('common.cancel')}</button>
          <button type="button" className="btn btn-primary" disabled={log.isPending} onClick={submit}>{t('pipeline.logModal.submit')}</button>
        </>
      }
    >
      <div className="modal-b">
        <div className="fgrid" style={{ gridTemplateColumns: '1fr 1fr' }}>
          <div className="field"><label htmlFor="lc-name">{t('pipeline.logModal.name')} <span className="req">*</span></label><input id="lc-name" type="text" value={v.name} onChange={(e) => set('name', e.target.value)} /></div>
          <div className="field"><label htmlFor="lc-email">{t('pipeline.logModal.email')} <span className="req">*</span></label><input id="lc-email" type="email" value={v.email} onChange={(e) => set('email', e.target.value)} /></div>
          <div className="field"><label htmlFor="lc-phone">{t('pipeline.logModal.phone')}</label><input id="lc-phone" type="tel" value={v.phone} onChange={(e) => set('phone', e.target.value)} /></div>
          <div className="field"><label htmlFor="lc-exp">{t('pipeline.logModal.experience')}</label><input id="lc-exp" type="number" min={0} step={0.5} value={v.experienceYears} onChange={(e) => set('experienceYears', Number(e.target.value))} /></div>
          <div className="field full">
            <label htmlFor="lc-source">{t('pipeline.logModal.source')} <span className="req">*</span></label>
            <select id="lc-source" value={v.source} onChange={(e) => set('source', e.target.value as CandidateSource)}>
              {(rules.data?.candidateSources ?? []).map((s) => <option key={s}>{s}</option>)}
            </select>
          </div>
        </div>
        <label className="chk-line" style={{ marginTop: 12 }}>
          <input type="checkbox" checked={v.privacyConsent} onChange={(e) => set('privacyConsent', e.target.checked)} />
          <span>{t('pipeline.logModal.consent')}</span>
        </label>
      </div>
    </Modal>
  )
}
