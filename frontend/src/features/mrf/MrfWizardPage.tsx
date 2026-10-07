import { useEffect, useMemo, useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { api } from '../../api/client'
import { useApiMutation, useRules } from '../../api/hooks'
import { can } from '../../auth/permissions'
import { useSession } from '../../auth/sessionContext'
import { useErrorToast, useToast } from '../../components/toastContext'
import { Chip, ErrorBox, Loading, PageHead } from '../../components/ui'
import { resolveDoa } from '../../domain/rules'
import type { EmploymentType, Grade, Requisition, RequisitionInput, RequisitionNature, RuleConfig } from '../../domain/types'
import { formatDate } from '../../utils/format'
import { validateMrf, type Field } from './validation'

const DRAFT_KEY = 'recuro.mrfDraft'
const GRADES: Grade[] = ['E', 'M1', 'M3', 'VP', 'KMP']
const EMPLOYMENT: EmploymentType[] = ['Permanent', 'Contractual', 'OffRoll']
const NATURES: RequisitionNature[] = ['NewPosition', 'Replacement', 'Backfill']

const emptyInput = (): RequisitionInput => ({
  department: '',
  designation: '',
  grade: '',
  location: '',
  positions: 1,
  reportingManager: '',
  employmentType: 'Permanent',
  nature: 'NewPosition',
  replacementReason: '',
  joiningDate: '',
  band: '',
  outOfBudget: false,
  oobJustification: '',
  qualifications: '',
  sourcingChannels: ['Employee Referral', 'Job Portals'],
})

function loadDraft(): RequisitionInput {
  try {
    const raw = localStorage.getItem(DRAFT_KEY)
    return raw ? { ...emptyInput(), ...(JSON.parse(raw) as Partial<RequisitionInput>) } : emptyInput()
  } catch {
    return emptyInput()
  }
}

export function MrfWizardPage() {
  const rules = useRules()
  if (rules.isPending) return <Loading />
  if (rules.isError) return <ErrorBox error={rules.error} />
  return <MrfWizard rules={rules.data} />
}

function MrfWizard({ rules }: { rules: RuleConfig }) {
  const { t } = useTranslation()
  const { user, actor } = useSession()
  const toast = useToast()
  const onError = useErrorToast()
  const [step, setStep] = useState<1 | 2 | 3>(1)
  const [v, setV] = useState<RequisitionInput>(loadDraft)
  const [showErrors, setShowErrors] = useState(false)
  const [submitted, setSubmitted] = useState<Requisition | null>(null)
  const create = useApiMutation((input: RequisitionInput) => api.createRequisition(actor, input))
  const canRaise = can(user.role, 'mrf.raise')

  // RCU-MRF-001: drafts auto-save.
  useEffect(() => {
    if (submitted) return
    try {
      localStorage.setItem(DRAFT_KEY, JSON.stringify(v))
    } catch {
      // Storage unavailable; drafts just won't survive a reload.
    }
  }, [v, submitted])

  const errors = useMemo(() => validateMrf(v), [v])
  const route = v.grade ? resolveDoa(rules, v.grade) : null
  const set = <K extends Field>(k: K, value: RequisitionInput[K]) => setV((prev) => ({ ...prev, [k]: value }))
  const err = (step: 1 | 2, f: Field) => showErrors && errors[step].includes(f)

  const chooseGrade = (g: Grade | '') => {
    const band = g ? resolveDoa(rules, g).bandLabel : ''
    setV((prev) => ({ ...prev, grade: g, band: rules.bands.some((b) => b.label === band) ? band : prev.band }))
  }

  const submit = () => {
    if (!canRaise) return toast(t('mrf.errors.onlyHrta'), 'warn')
    if (errors[1].length || errors[2].length) {
      setShowErrors(true)
      setStep(errors[1].length ? 1 : 2)
      return toast(t('mrf.errors.incomplete'), 'warn')
    }
    create.mutate(v, {
      onSuccess: (req) => {
        setSubmitted(req)
        try {
          localStorage.removeItem(DRAFT_KEY)
        } catch {
          // ignore
        }
        toast(t('mrf.success', { reqId: req.reqId, approver: req.route.approving, target: formatDate(req.targetClosure) }), 'move')
      },
      onError,
    })
  }

  const reset = () => {
    setSubmitted(null)
    setV(emptyInput())
    setShowErrors(false)
    setStep(1)
  }

  const label = (f: string, required = true, hint?: string) => (
    <label htmlFor={`mrf-${f}`}>
      {t(`mrf.fields.${f}`)} {required && <span className="req">*</span>} {hint && <span className="hint">{hint}</span>}
    </label>
  )
  const options = (items: string[]) => (
    <>
      <option value="">{t('mrf.select')}</option>
      {items.map((o) => (
        <option key={o}>{o}</option>
      ))}
    </>
  )
  const fieldErr = (s: 1 | 2, f: Field) => err(s, f) && <span className="err-text">{t('mrf.errors.required')}</span>

  return (
    <div className="screen">
      <PageHead title={t('mrf.title')} policy={t('mrf.policy')} crumb={t('mrf.crumb')}>
        <Chip tone="navy">{submitted ? submitted.reqId : t('mrf.autoId')}</Chip>
        <Chip tone={submitted ? 'amber' : 'slate'}>
          {submitted ? t('mrf.status.submitted', { reqId: submitted.reqId, approver: submitted.route.approving }) : t('mrf.status.draft')}
        </Chip>
      </PageHead>
      {!canRaise && (
        <div className="s02-note" role="note">
          👁 <span><Trans i18nKey="mrf.approverNote" components={{ b: <b /> }} /></span>
        </div>
      )}
      <ol className="wizard" aria-label="Steps" style={{ listStyle: 'none' }}>
        {([1, 2, 3] as const).map((n) => (
          <li key={n} style={{ display: 'flex', alignItems: 'center' }}>
            <button
              type="button"
              className={`wstep ${step === n ? 'active' : ''} ${step > n ? 'done' : ''}`}
              style={{ background: 'none', border: 'none' }}
              aria-current={step === n ? 'step' : undefined}
              onClick={() => setStep(n)}
            >
              <span className="wdot">{n}</span>
              <span className="wlbl">{t(`mrf.steps.${n}`)}</span>
            </button>
            {n < 3 && <span className={`wline ${step > n ? 'done' : ''}`} />}
          </li>
        ))}
      </ol>

      <fieldset disabled={!canRaise || !!submitted} style={{ border: 'none' }}>
        {step === 1 && (
          <div className="wpanel card" style={{ padding: 22 }}>
            <div className="fgrid">
              <div className={`field ${err(1, 'department') ? 'err' : ''}`}>
                {label('department')}
                <select id="mrf-department" value={v.department} onChange={(e) => set('department', e.target.value)}>{options(rules.departments)}</select>
                {fieldErr(1, 'department')}
              </div>
              <div className={`field ${err(1, 'designation') ? 'err' : ''}`}>
                {label('designation')}
                <select id="mrf-designation" value={v.designation} onChange={(e) => set('designation', e.target.value)}>{options(rules.designations)}</select>
                {fieldErr(1, 'designation')}
              </div>
              <div className={`field ${err(1, 'grade') ? 'err' : ''}`}>
                {label('grade', true, t('mrf.fields.gradeHint'))}
                <select id="mrf-grade" value={v.grade} onChange={(e) => chooseGrade(e.target.value as Grade | '')}>
                  <option value="">{t('mrf.select')}</option>
                  {GRADES.map((g) => (
                    <option key={g} value={g}>{t(`mrf.grades.${g}`)}</option>
                  ))}
                </select>
                {fieldErr(1, 'grade')}
              </div>
              <div className={`field ${err(1, 'location') ? 'err' : ''}`}>
                {label('location')}
                <select id="mrf-location" value={v.location} onChange={(e) => set('location', e.target.value)}>{options(rules.locations)}</select>
                {fieldErr(1, 'location')}
              </div>
              <div className={`field ${err(1, 'positions') ? 'err' : ''}`}>
                {label('positions')}
                <input id="mrf-positions" type="number" min={1} value={v.positions} onChange={(e) => set('positions', Number(e.target.value))} />
                {fieldErr(1, 'positions')}
              </div>
              <div className={`field ${err(1, 'reportingManager') ? 'err' : ''}`}>
                {label('reportingManager')}
                <select id="mrf-reportingManager" value={v.reportingManager} onChange={(e) => set('reportingManager', e.target.value)}>{options(rules.reportingManagers)}</select>
                {fieldErr(1, 'reportingManager')}
              </div>
              <div className="field full" role="radiogroup" aria-label={t('mrf.fields.employmentType')}>
                <label>{t('mrf.fields.employmentType')}</label>
                <div className="pills">
                  {EMPLOYMENT.map((e) => (
                    <span key={e}>
                      <input type="radio" name="emptype" id={`et-${e}`} checked={v.employmentType === e} onChange={() => set('employmentType', e)} />
                      <label htmlFor={`et-${e}`}>{t(`mrf.employmentTypes.${e}`)}</label>
                    </span>
                  ))}
                </div>
              </div>
              <div className="field full" role="radiogroup" aria-label={t('mrf.fields.nature')}>
                <label>{t('mrf.fields.nature')}</label>
                <div className="pills">
                  {NATURES.map((n) => (
                    <span key={n}>
                      <input type="radio" name="nature" id={`nt-${n}`} checked={v.nature === n} onChange={() => set('nature', n)} />
                      <label htmlFor={`nt-${n}`}>{t(`mrf.natures.${n}`)}</label>
                    </span>
                  ))}
                </div>
              </div>
              {v.nature !== 'NewPosition' && (
                <div className={`field full ${err(1, 'replacementReason') ? 'err' : ''}`}>
                  {label('replacementReason')}
                  <input id="mrf-replacementReason" type="text" placeholder={t('mrf.fields.replacementPlaceholder')} value={v.replacementReason} onChange={(e) => set('replacementReason', e.target.value)} />
                  {fieldErr(1, 'replacementReason')}
                </div>
              )}
              <div className={`field ${err(1, 'joiningDate') ? 'err' : ''}`}>
                {label('joiningDate')}
                <input id="mrf-joiningDate" type="date" value={v.joiningDate} onChange={(e) => set('joiningDate', e.target.value)} />
                {fieldErr(1, 'joiningDate')}
              </div>
            </div>
            <div className="wfoot">
              <span style={{ fontSize: 11, color: 'var(--muted)' }}>
                <Trans i18nKey="common.mandatoryNote" components={{ req: <span style={{ color: 'var(--red)' }} /> }} />
              </span>
              <button type="button" className="btn btn-primary" onClick={() => setStep(2)}>{t('mrf.next1')}</button>
            </div>
          </div>
        )}

        {step === 2 && (
          <div className="wpanel card" style={{ padding: 22 }}>
            <div className="fgrid">
              <div className={`field ${err(2, 'band') ? 'err' : ''}`}>
                {label('band', true, t('mrf.fields.bandHint'))}
                <select id="mrf-band" value={v.band} onChange={(e) => set('band', e.target.value)}>{options(rules.bands.map((b) => b.label))}</select>
                {fieldErr(2, 'band')}
              </div>
              <div className="field full" role="radiogroup" aria-label={t('mrf.fields.budget')}>
                <label>{t('mrf.fields.budget')} <span className="req">*</span></label>
                <div className="pills">
                  <input type="radio" name="budget" id="bg-in" checked={!v.outOfBudget} onChange={() => set('outOfBudget', false)} />
                  <label htmlFor="bg-in">{t('mrf.fields.inBudget')}</label>
                  <input type="radio" name="budget" id="bg-out" checked={v.outOfBudget} onChange={() => set('outOfBudget', true)} />
                  <label htmlFor="bg-out">{t('mrf.fields.outOfBudget')}</label>
                </div>
              </div>
              {v.outOfBudget && (
                <div className={`field full ${err(2, 'oobJustification') ? 'err' : ''}`}>
                  {label('oobJustification', true, t('mrf.fields.oobHint'))}
                  <textarea id="mrf-oobJustification" rows={2} placeholder={t('mrf.fields.oobPlaceholder')} value={v.oobJustification} onChange={(e) => set('oobJustification', e.target.value)} />
                  {fieldErr(2, 'oobJustification')}
                </div>
              )}
              <div className={`field full ${err(2, 'qualifications') ? 'err' : ''}`}>
                {label('qualifications')}
                <textarea id="mrf-qualifications" rows={3} value={v.qualifications} onChange={(e) => set('qualifications', e.target.value)} />
                {fieldErr(2, 'qualifications')}
              </div>
              <div className="field full">
                <label>{t('mrf.fields.channels')}</label>
                <div className="pills chk">
                  {rules.sourcingChannels.map((c, i) => (
                    <span key={c}>
                      <input
                        type="checkbox"
                        id={`sc-${i}`}
                        checked={v.sourcingChannels.includes(c)}
                        onChange={(e) => set('sourcingChannels', e.target.checked ? [...v.sourcingChannels, c] : v.sourcingChannels.filter((x) => x !== c))}
                      />
                      <label htmlFor={`sc-${i}`}>{c}</label>
                    </span>
                  ))}
                </div>
                <span className="hint">{t('mrf.fields.channelsHint')}</span>
              </div>
            </div>
            <div className="wfoot">
              <button type="button" className="btn btn-ghost" onClick={() => setStep(1)}>{t('common.back')}</button>
              <button type="button" className="btn btn-primary" onClick={() => setStep(3)}>{t('mrf.next2')}</button>
            </div>
          </div>
        )}
      </fieldset>

      {step === 3 && (
        <div className="wpanel card" style={{ padding: 22 }}>
          <h3 style={{ color: 'var(--navy)', fontSize: 14, marginBottom: 12 }}>{t('mrf.routing.title')}</h3>
          {route ? (
            <>
              <table className="mini">
                <thead>
                  <tr><th>{t('mrf.routing.stage')}</th><th>{t('mrf.routing.authority')}</th><th>{t('mrf.routing.sla')}</th></tr>
                </thead>
                <tbody>
                  <tr><td><b>{t('mrf.routing.initiating')}</b></td><td>{route.initiating}</td><td>{t('mrf.routing.raise')}</td></tr>
                  <tr><td><b>{t('mrf.routing.recommending')}</b></td><td>{route.recommending}</td><td>{t('mrf.routing.oneDay')}</td></tr>
                  <tr><td><b>{t('mrf.routing.final')}</b></td><td>{v.outOfBudget && route.approverRole !== 'mdceo' ? `${route.approving} + MD/CEO` : route.approving}</td><td>{t('mrf.routing.finalSla')}</td></tr>
                </tbody>
              </table>
              <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', marginTop: 14 }}>
                <Chip tone="teal">{t('mrf.routing.tat', { label: route.overallTat.label })}</Chip>
                <Chip tone="navy">{t('mrf.routing.band', { band: v.band || route.bandLabel })}</Chip>
                {v.grade === 'KMP' && <Chip tone="purple">{t('mrf.routing.kmp')}</Chip>}
                {v.outOfBudget && <Chip tone="amber">{t('mrf.routing.oob')}</Chip>}
              </div>
            </>
          ) : (
            <p className="subnote">{t('mrf.routing.pickGrade')}</p>
          )}
          <div className="route-note">{t('mrf.routing.note')}</div>
          <div className="wfoot">
            <button type="button" className="btn btn-ghost" onClick={() => setStep(2)}>{t('common.back')}</button>
            <div style={{ display: 'flex', gap: 10 }}>
              {submitted ? (
                <button type="button" className="btn btn-ghost" onClick={reset}>{t('mrf.raiseAnother')}</button>
              ) : (
                <button type="button" className="btn btn-ghost" disabled={!canRaise} onClick={() => toast(t('common.draftSaved'))}>{t('common.saveDraft')}</button>
              )}
              <button type="button" className="btn btn-gold" disabled={!canRaise || !!submitted || create.isPending} onClick={submit}>
                {submitted ? t('mrf.submitted') : t('mrf.submit')}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
