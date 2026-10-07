import { useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { api } from '../../api/client'
import { useApiMutation, useJobDescription, useRules } from '../../api/hooks'
import { can } from '../../auth/permissions'
import { useSession } from '../../auth/sessionContext'
import { useErrorToast, useToast } from '../../components/toastContext'
import { Card, Chip, ErrorBox, Loading, PageHead } from '../../components/ui'
import type { JobDescription } from '../../domain/types'
import { validateJd } from './validation'

const REQ_ID = 'REQ-2026-0156'

export function JdBuilderPage() {
  const query = useJobDescription(REQ_ID)
  const rules = useRules()
  if (query.isPending || rules.isPending) return <Loading />
  if (query.isError) return <ErrorBox error={query.error} />
  if (rules.isError) return <ErrorBox error={rules.error} />
  return <JdBuilder key={`${query.data.version}-${query.data.status}`} initial={query.data} competencies={rules.data.competencies} />
}

function JdBuilder({ initial, competencies }: { initial: JobDescription; competencies: { id: string; name: string }[] }) {
  const { t } = useTranslation()
  const { user, actor } = useSession()
  const toast = useToast()
  const onError = useErrorToast()
  const [jd, setJd] = useState(initial)
  const editable = can(user.role, 'jd.edit')
  const submit = useApiMutation((d: JobDescription) => api.submitJobDescription(actor, d))
  const assessmentOptions = t('jd.assessments', { returnObjects: true }) as string[]

  const set = <K extends keyof JobDescription>(k: K, v: JobDescription[K]) => setJd((p) => ({ ...p, [k]: v }))
  const toggle = (k: 'competencies' | 'assessments', v: string, on: boolean) =>
    set(k, on ? [...jd[k], v] : jd[k].filter((x) => x !== v))

  const onSubmit = () => {
    const problem = validateJd(jd)
    if (problem) return toast(t(problem), 'warn')
    submit.mutate(jd, { onSuccess: (next) => toast(t('jd.submitted', { version: next.version }), 'move'), onError })
  }

  return (
    <div className="screen">
      <PageHead title={t('jd.title')} policy={t('jd.policy')} crumb={t('jd.crumb', { reqId: jd.reqId })}>
        {!editable && <Chip tone="slate">{t('jd.readOnly')}</Chip>}
        <Chip tone={jd.status === 'Draft' ? 'amber' : 'green'}>{t(`jd.status.${jd.status}`, { version: jd.version })}</Chip>
      </PageHead>
      <div className="jd-grid">
        <div className="col">
          <Card style={{ padding: '20px 22px' }}>
            <fieldset disabled={!editable} style={{ border: 'none' }}>
              <div className="field" style={{ marginBottom: 16 }}>
                <label htmlFor="jd-purpose">{t('jd.purpose')} <span className="req">*</span> <span className="hint">{t('jd.purposeHint')}</span></label>
                <textarea id="jd-purpose" rows={2} value={jd.purpose} onChange={(e) => set('purpose', e.target.value)} />
              </div>
              <div className="field" style={{ marginBottom: 16 }}>
                <label>{t('jd.responsibilities')} <span className="req">*</span> <span className="hint">{t('jd.responsibilitiesHint')}</span></label>
                <div>
                  {jd.responsibilities.map((r, i) => (
                    <div key={i} className="resp-row">
                      <input
                        type="text"
                        aria-label={t('jd.responsibilityLabel', { n: i + 1 })}
                        placeholder={t('jd.responsibilityPlaceholder')}
                        value={r}
                        onChange={(e) => set('responsibilities', jd.responsibilities.map((x, j) => (j === i ? e.target.value : x)))}
                      />
                      <button type="button" aria-label={t('jd.remove', { n: i + 1 })} onClick={() => set('responsibilities', jd.responsibilities.filter((_, j) => j !== i))}>✕</button>
                    </div>
                  ))}
                </div>
                <div>
                  <button
                    type="button"
                    className="btn btn-ghost btn-sm"
                    onClick={() => (jd.responsibilities.length >= 6 ? toast(t('jd.max'), 'warn') : set('responsibilities', [...jd.responsibilities, '']))}
                  >
                    {t('jd.add')}
                  </button>
                </div>
              </div>
              <div className="fgrid" style={{ marginBottom: 16 }}>
                <div className="field"><label htmlFor="jd-reports">{t('jd.reportsTo')}</label><input id="jd-reports" type="text" value={jd.reportsTo} onChange={(e) => set('reportsTo', e.target.value)} /></div>
                <div className="field">
                  <label htmlFor="jd-team">{t('jd.teamSize')}</label>
                  <select id="jd-team" value={jd.teamSize} onChange={(e) => set('teamSize', e.target.value)}>
                    <option>None (Individual Contributor)</option><option>1–5</option><option>6–15</option>
                  </select>
                </div>
                <div className="field"><label htmlFor="jd-loc">{t('jd.location')}</label><input id="jd-loc" type="text" value={jd.location} onChange={(e) => set('location', e.target.value)} /></div>
                <div className="field"><label htmlFor="jd-qual">{t('jd.minQualification')}</label><input id="jd-qual" type="text" value={jd.minQualification} onChange={(e) => set('minQualification', e.target.value)} /></div>
                <div className="field"><label htmlFor="jd-exp">{t('jd.experience')}</label><input id="jd-exp" type="text" value={jd.experience} onChange={(e) => set('experience', e.target.value)} /></div>
                <div className="field">
                  <label htmlFor="jd-grade">{t('jd.grade')}</label>
                  <select id="jd-grade" value={jd.grade} onChange={(e) => set('grade', e.target.value)}><option>M1</option><option>M3</option><option>VP</option></select>
                </div>
              </div>
              <div className="field" style={{ marginBottom: 4 }}>
                <span style={{ fontSize: 12, fontWeight: 700, color: 'var(--navy)' }}>
                  {t('jd.competencies')} <span className="req">*</span> <span className="hint">{t('jd.competenciesHint')}</span>{' '}
                  <Chip tone="navy">{t('jd.selected', { count: jd.competencies.length })}</Chip>
                </span>
                <div className="comp-grid">
                  {competencies.map((c) => (
                    <label key={c.id} className="chk-line">
                      <input type="checkbox" checked={jd.competencies.includes(c.id)} onChange={(e) => toggle('competencies', c.id, e.target.checked)} /> {c.name}
                    </label>
                  ))}
                </div>
              </div>
            </fieldset>
          </Card>
          <Card title={t('jd.assessmentTitle')} sub={t('jd.assessmentSub')}>
            <fieldset disabled={!editable} style={{ border: 'none', padding: '14px 18px' }}>
              {assessmentOptions.map((a) => (
                <label key={a} className="chk-line">
                  <input type="checkbox" checked={jd.assessments.includes(a)} onChange={(e) => toggle('assessments', a, e.target.checked)} /> {a}
                </label>
              ))}
              <div className="fgrid" style={{ marginTop: 10 }}>
                <div className="field"><label htmlFor="jd-bench">{t('jd.benchmark')}</label><input id="jd-bench" type="text" value={jd.benchmark} onChange={(e) => set('benchmark', e.target.value)} /></div>
                <div className="field"><label htmlFor="jd-owner">{t('jd.owner')}</label><input id="jd-owner" type="text" value={t('jd.ownerValue')} readOnly style={{ background: '#F8FAFC' }} /></div>
              </div>
            </fieldset>
          </Card>
          <div className="wfoot" style={{ borderTop: 'none', paddingTop: 4 }}>
            <span className="subnote">{t('jd.footNote')}</span>
            {editable && (
              <div style={{ display: 'flex', gap: 10 }}>
                <button type="button" className="btn btn-ghost" onClick={() => toast(t('common.draftSaved'))}>{t('common.saveDraft')}</button>
                <button type="button" className="btn btn-gold" disabled={submit.isPending || jd.status !== 'Draft'} onClick={onSubmit}>{t('jd.submit')}</button>
              </div>
            )}
          </div>
        </div>
        <div className="col">
          <Card title={t('jd.bench.title')} sub={t('jd.bench.sub')} aside={<Chip tone="green">{t('jd.bench.matches', { grade: jd.grade })}</Chip>}>
            <div className="bench-wrap">
              <div style={{ fontSize: 12, color: 'var(--muted)' }}>{t('jd.bench.scale')}</div>
              <div className="bench-bar"><i /></div>
              <div className="bench-scale"><span>M1</span><span>M2</span><span>M3</span><span>M4</span><span>M5</span></div>
              <div className="subnote" style={{ marginTop: 8 }}>
                <Trans i18nKey="jd.bench.mapping" values={{ grade: `${jd.grade} · ₹18–24L` }} components={{ b: <b /> }} />
              </div>
            </div>
          </Card>
          <Card title={t('jd.sourcing.title')} sub={t('jd.sourcing.sub')}>
            <div className="src-chips">
              <Chip tone="gold">Employee Referral ★</Chip><Chip tone="navy">Job Portals ★</Chip>
              <Chip tone="purple">IJP (5-day window)</Chip><Chip tone="teal">Consultants (niche)</Chip>
            </div>
          </Card>
          <Card title={t('jd.history')}>
            <div style={{ padding: '8px 18px 14px' }}>
              {jd.history.map((h) => (
                <div key={h.version} className="ver-item">
                  <span><b>v{h.version}</b> · {h.note}</span>
                  <Chip tone="slate">{h.at} · {h.by}</Chip>
                </div>
              ))}
            </div>
          </Card>
        </div>
      </div>
    </div>
  )
}
