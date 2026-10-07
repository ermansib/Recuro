import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { api } from '../../api/client'
import { useApiMutation, useInterview, useJobDescription, usePipeline, useRules } from '../../api/hooks'
import { can } from '../../auth/permissions'
import { useSession } from '../../auth/sessionContext'
import { useErrorToast, useToast } from '../../components/toastContext'
import { Chip, ErrorBox, Loading, PageHead, Sensitive } from '../../components/ui'
import { assessmentAverage, RECOMMEND_THRESHOLD } from '../../domain/rules'
import type { InterviewRound, PipelineCard, Recommendation, RuleConfig } from '../../domain/types'
import { formatDate, formatTime, initials, relativeTime } from '../../utils/format'

const APP_ID = 'APP-2026-0386'
const RECOMMENDATIONS: Recommendation[] = ['StronglyRecommend', 'Recommend', 'Reservations', 'DoNotRecommend']

export function AssessmentPage() {
  const interview = useInterview(APP_ID)
  const pipeline = usePipeline('REQ-2026-0156')
  const rules = useRules()
  const jd = useJobDescription('REQ-2026-0156')
  if (interview.isPending || pipeline.isPending || rules.isPending || jd.isPending) return <Loading />
  const error = interview.error ?? pipeline.error ?? rules.error ?? jd.error
  if (error) return <ErrorBox error={error} />
  const card = pipeline.data?.find((c) => c.application.appId === APP_ID)
  if (!card || !interview.data || !rules.data || !jd.data) return <ErrorBox error={new Error('Candidate not found')} />
  // RCU-ASM-002: Annexure B shows the JD's competencies (plus any the panel already rated).
  const allowed = new Set([...jd.data.competencies, ...interview.data.ratings.map((r) => r.competencyId)])
  return <AssessmentForm key={interview.data.status} initial={interview.data} card={card} rules={rules.data} allowed={allowed} />
}

function AssessmentForm({ initial, card, rules, allowed }: { initial: InterviewRound; card: PipelineCard; rules: RuleConfig; allowed: Set<string> }) {
  const { t } = useTranslation()
  const { user, actor } = useSession()
  const toast = useToast()
  const onError = useErrorToast()
  const [round, setRound] = useState(() => withAllCompetencies(initial, rules, allowed))
  const submit = useApiMutation((r: InterviewRound) => api.submitInterview(actor, r))
  const locked = round.status === 'Submitted'
  const editable = can(user.role, 'assessment.submit') && !locked
  const avg = assessmentAverage(round.ratings)
  const c = card.candidate

  const setRating = (i: number, patch: Partial<InterviewRound['ratings'][number]>) =>
    setRound((r) => ({ ...r, ratings: r.ratings.map((x, j) => (j === i ? { ...x, ...patch } : x)) }))

  const onSubmit = () => {
    if (round.ratings.some((r) => !r.na && r.score === null)) return toast(t('assessment.errors.unrated'), 'warn')
    if (!round.recommendation) return toast(t('assessment.errors.recommendation'), 'warn')
    if (!round.justification.trim()) return toast(t('assessment.errors.justification'), 'warn')
    submit.mutate(round, { onSuccess: () => toast(t('assessment.done'), 'move'), onError })
  }

  const compName = (id: string) => rules.competencies.find((x) => x.id === id)

  return (
    <div className="screen">
      <PageHead
        title={t('assessment.title')}
        policy={t('assessment.policy')}
        crumb={t('assessment.crumb', { reqId: card.application.reqId, name: c.name, round: round.round })}
      >
        <Link to="/pipeline" className="btn btn-ghost">{t('assessment.back')}</Link>
      </PageHead>
      <div className="assess-grid">
        <aside className="card cand-card">
          <div className="big-av" aria-hidden="true">{initials(c.name)}</div>
          <h2>{c.name}</h2>
          <div className="cpos">{t('assessment.candidate', { position: 'Sr Manager — Credit' })}</div>
          <div className="fact"><span>{t('assessment.facts.requisition')}</span><b>{card.application.reqId}</b></div>
          <div className="fact"><span>{t('assessment.facts.source')}</span><b>{c.source}</b></div>
          <div className="fact"><span>{t('assessment.facts.experience')}</span><b>{t('assessment.facts.years', { years: c.experienceYears })}</b></div>
          <div className="fact"><span>{t('assessment.facts.ctc')}</span><b><Sensitive>₹{c.currentCtc?.toFixed(1)}L</Sensitive></b></div>
          <div className="fact"><span>{t('assessment.facts.contact')}</span><b><Sensitive>{c.phone.replace(/\d(?=\d{4})/g, '•')}</Sensitive></b></div>
          <div className="fact"><span>{t('assessment.facts.notice')}</span><b>{t('assessment.facts.noticeDays', { days: c.noticeDays })}</b></div>
          <div className="mini-tl">
            <h4>{t('assessment.history')}</h4>
            {round.history.map((h) => (
              <div key={h.round} className="mtl-row"><span>{h.round}</span><Chip tone={h.tone}>{h.status}</Chip></div>
            ))}
          </div>
          <div className="timer-chip">{t('assessment.timer', { due: relativeTime(round.feedbackDueAt) })}</div>
        </aside>
        <div className="card form-card">
          <div className="form-head">
            <h3>{round.round}</h3>
            <Chip tone="navy">{t('assessment.interviewer', { name: round.interviewer })}</Chip>
            <Chip tone="slate">📅 {formatDate(round.scheduledFor)} · {formatTime(round.scheduledFor)}</Chip>
            <Chip tone="purple">{t(`assessment.modes.${round.mode}`)}</Chip>
            {!can(user.role, 'assessment.submit') && <Chip tone="slate">{t('assessment.readOnly')}</Chip>}
            {locked && <Chip tone="green">{t('assessment.locked', { at: round.submittedAt ? formatTime(round.submittedAt) : '' })}</Chip>}
          </div>
          <fieldset className="form-body" disabled={!editable} style={{ border: 'none' }}>
            {round.ratings.map((r, i) => {
              const comp = compName(r.competencyId)
              const name = comp?.name ?? r.competencyId
              return (
                <div key={r.competencyId} className={`rate-row ${r.na ? 'na' : ''}`}>
                  <div className="rr-name">
                    <b>{name}</b>
                    <span>{comp?.hint}</span>
                    <label className="na-toggle">
                      <input type="checkbox" checked={r.na} onChange={(e) => setRating(i, { na: e.target.checked })} /> {t('assessment.markNa')}
                    </label>
                  </div>
                  <div className="seg" role="radiogroup" aria-label={t('assessment.scoreLabel', { competency: name })}>
                    {[1, 2, 3, 4, 5].map((n) => (
                      <span key={n}>
                        <input type="radio" name={`r-${r.competencyId}`} id={`r-${r.competencyId}-${n}`} checked={r.score === n} disabled={r.na} onChange={() => setRating(i, { score: n })} />
                        <label htmlFor={`r-${r.competencyId}-${n}`}>{n}</label>
                      </span>
                    ))}
                  </div>
                  <div className="rr-note">
                    <input type="text" aria-label={t('assessment.commentLabel', { competency: name })} value={r.comment} onChange={(e) => setRating(i, { comment: e.target.value })} />
                  </div>
                </div>
              )
            })}
            <div className="avg-strip">
              <div>
                <span className="lbl">{t('assessment.average')}</span>
                <div className="hint">{t('assessment.averageHint')}</div>
              </div>
              <span className="val" aria-live="polite">{avg?.toFixed(1) ?? '—'}</span>
              <Chip tone={avg !== null && avg >= RECOMMEND_THRESHOLD ? 'green' : 'amber'}>{t('assessment.threshold', { value: RECOMMEND_THRESHOLD })}</Chip>
            </div>
            <span className="field-label">{t('assessment.recommendation')} *</span>
            <div className="reco-seg" role="radiogroup" aria-label={t('assessment.recommendation')}>
              {RECOMMENDATIONS.map((rec) => (
                <span key={rec}>
                  <input type="radio" name="reco" id={`rc-${rec}`} className={rec === 'DoNotRecommend' ? 'danger' : ''} checked={round.recommendation === rec} onChange={() => setRound((x) => ({ ...x, recommendation: rec }))} />
                  <label htmlFor={`rc-${rec}`}>{t(`assessment.recommendations.${rec}`)}</label>
                </span>
              ))}
            </div>
            <label className="field-label" htmlFor="as-just">{t('assessment.justification')} *</label>
            <textarea id="as-just" value={round.justification} onChange={(e) => setRound((x) => ({ ...x, justification: e.target.value }))} />
          </fieldset>
          <div className="form-foot">
            <span className="note">{t('assessment.foot')}</span>
            <div style={{ display: 'flex', gap: 10 }}>
              <button type="button" className="btn btn-ghost" disabled={!editable} onClick={() => toast(t('assessment.draftSaved'))}>{t('common.saveDraft')}</button>
              <button type="button" className="btn btn-primary" disabled={!editable || submit.isPending} style={locked ? { background: 'var(--green)' } : undefined} onClick={onSubmit}>
                {locked ? t('assessment.submitted') : t('assessment.submit')}
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}

/** Adds unrated rows for JD competencies that the round doesn't cover yet. */
function withAllCompetencies(round: InterviewRound, rules: RuleConfig, allowed: Set<string>): InterviewRound {
  const have = new Set(round.ratings.map((r) => r.competencyId))
  const extra = rules.competencies
    .filter((c) => allowed.has(c.id) && !have.has(c.id))
    .map((c) => ({ competencyId: c.id, score: null, na: false, comment: '' }))
  return { ...round, ratings: [...round.ratings, ...extra] }
}
