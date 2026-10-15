import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { employeeApi } from '../../api/client'
import { useApiMutation, useInternalJobs, useMyReferrals } from '../../api/hooks'
import { useCan } from '../../auth/permissions'
import { useSession } from '../../auth/sessionContext'
import { useErrorToast, useToast } from '../../components/toastContext'
import { Card, Chip, ErrorBox, Loading, PageHead } from '../../components/ui'
import type { InternalJob, ReferralInput } from '../../domain/types'
import { formatDate } from '../../utils/format'

const empty = (): ReferralInput => ({ reqId: '', name: '', email: '', phone: '', experienceYears: null, relationship: '', coiAccepted: false })

/** Employee home (S-16, RCU-EMP-001/003/005): internal jobs HR opened to employees, refer someone, and my referrals. */
export function EmployeePage() {
  const { t } = useTranslation()
  const { user, tenant, token } = useSession()
  const can = useCan()
  const toast = useToast()
  const onError = useErrorToast()
  const jobs = useInternalJobs()
  const referrals = useMyReferrals()
  const [form, setForm] = useState(empty)
  const submit = useApiMutation((v: ReferralInput) => employeeApi.submitReferral(token, v))
  const set = <K extends keyof ReferralInput>(k: K, v: ReferralInput[K]) => setForm((f) => ({ ...f, [k]: v }))

  if (jobs.isPending) return <Loading />
  if (jobs.isError) return <ErrorBox error={jobs.error} />

  const canRefer = can('referral.submit')
  const selected = jobs.data.find((j) => j.reqId === form.reqId)
  const titleOf = (reqId: string) => jobs.data.find((j) => j.reqId === reqId)?.title ?? reqId

  const onSubmit = (e: FormEvent) => {
    e.preventDefault()
    if (!form.reqId) return toast(t('employee.refer.needJob'), 'warn')
    if (!form.name.trim() || !form.email.trim() || !form.relationship.trim()) return toast(t('employee.refer.needFields'), 'warn')
    if (!form.coiAccepted) return toast(t('employee.refer.needCoi'), 'warn')
    submit.mutate(form, {
      onSuccess: () => {
        toast(t('employee.refer.done', { name: form.name.trim() }), 'move')
        setForm(empty())
      },
      onError,
    })
  }

  return (
    <div className="screen">
      <PageHead title={t('employee.title', { name: user.name })} crumb={t('employee.sub', { workspace: tenant.name })} />

      <Card title={t('employee.jobs.title')} sub={t('employee.jobs.sub', { workspace: tenant.name })}>
        <div style={{ padding: '4px 20px 16px' }}>
          {jobs.data.length === 0 && <div className="empty">{t('employee.jobs.empty')}</div>}
          {jobs.data.map((j) => (
            <JobRow key={j.reqId} job={j} canRefer={canRefer} onRefer={() => set('reqId', j.reqId)} />
          ))}
        </div>
      </Card>

      {canRefer && (
        <Card style={{ marginTop: 20 }} title={t('employee.refer.title')} sub={selected ? t('employee.refer.for', { job: selected.title }) : t('employee.refer.chooseJob')}>
          <form onSubmit={onSubmit} style={{ padding: '16px 20px' }} noValidate>
            <div className="fgrid">
              <Field id="ref-job" label={t('employee.jobs.title')}>
                <select id="ref-job" value={form.reqId} onChange={(e) => set('reqId', e.target.value)}>
                  <option value="">{t('employee.refer.chooseJob')}</option>
                  {jobs.data.map((j) => <option key={j.reqId} value={j.reqId}>{j.title}</option>)}
                </select>
              </Field>
              <Field id="ref-name" label={t('employee.refer.name')}>
                <input id="ref-name" value={form.name} onChange={(e) => set('name', e.target.value)} />
              </Field>
              <Field id="ref-email" label={t('employee.refer.email')}>
                <input id="ref-email" type="email" value={form.email} onChange={(e) => set('email', e.target.value)} />
              </Field>
              <Field id="ref-phone" label={t('employee.refer.phone')}>
                <input id="ref-phone" type="tel" value={form.phone} onChange={(e) => set('phone', e.target.value)} />
              </Field>
              <Field id="ref-exp" label={t('employee.refer.experience')}>
                <input id="ref-exp" type="number" min={0} value={form.experienceYears?.toString() ?? ''} onChange={(e) => set('experienceYears', e.target.value === '' ? null : Number(e.target.value))} />
              </Field>
              <Field id="ref-rel" label={t('employee.refer.relationship')}>
                <input id="ref-rel" value={form.relationship} onChange={(e) => set('relationship', e.target.value)} />
              </Field>
            </div>
            <label style={{ display: 'flex', gap: 8, alignItems: 'center', margin: '12px 0', fontSize: 13 }}>
              <input type="checkbox" checked={form.coiAccepted} onChange={(e) => set('coiAccepted', e.target.checked)} />
              {t('employee.refer.coi')}
            </label>
            <button type="submit" className="btn btn-primary" disabled={submit.isPending}>{t('employee.refer.submit')}</button>
          </form>
        </Card>
      )}

      {canRefer && (
        <Card style={{ marginTop: 20 }} title={t('employee.mine.title')}>
          <div style={{ padding: '4px 20px 16px' }}>
            {referrals.isPending && <Loading />}
            {referrals.isError && <ErrorBox error={referrals.error} />}
            {referrals.data?.length === 0 && <div className="empty">{t('employee.mine.empty')}</div>}
            {referrals.data && referrals.data.length > 0 && (
              <table className="mini">
                <thead>
                  <tr>
                    <th>{t('employee.mine.candidate')}</th>
                    <th>{t('employee.mine.job')}</th>
                    <th>{t('employee.mine.status')}</th>
                    <th>{t('employee.mine.submitted')}</th>
                  </tr>
                </thead>
                <tbody>
                  {referrals.data.map((r) => (
                    <tr key={r.id}>
                      <td>{r.candidateName}</td>
                      <td>{titleOf(r.reqId)}</td>
                      <td>
                        <Chip tone="slate">{r.progress || r.status}</Chip> {r.bonusEligible && <Chip tone="gold">{t('employee.mine.bonus')}</Chip>}
                      </td>
                      <td>{formatDate(r.submittedAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </Card>
      )}
    </div>
  )
}

function Field({ id, label, children }: { id: string; label: string; children: React.ReactNode }) {
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      {children}
    </div>
  )
}

function JobRow({ job, canRefer, onRefer }: { job: InternalJob; canRefer: boolean; onRefer: () => void }) {
  const { t } = useTranslation()
  const meta = [job.location, job.department, job.grade].filter(Boolean)
  return (
    <div className="job-card">
      <div className="jc-main">
        <b>{job.title}</b>
        <div className="jc-meta">
          {meta.map((m, i) => <span key={m}>{i > 0 && '· '}{m}</span>)}
          <Chip tone="slate">{t('employee.jobs.closes', { date: formatDate(job.closesAt) })}</Chip>
          {job.minTenureMonths > 0 && <Chip tone="navy">{t('employee.jobs.tenure', { months: job.minTenureMonths })}</Chip>}
          {job.eligibleGrades.length > 0 && <Chip tone="navy">{t('employee.jobs.grades', { grades: job.eligibleGrades.join(', ') })}</Chip>}
        </div>
        {job.summary && <p className="subnote">{job.summary}</p>}
      </div>
      {canRefer && <button type="button" className="btn btn-primary" onClick={onRefer}>{t('employee.jobs.refer')}</button>}
    </div>
  )
}
