import { useRef, useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { useOutletContext } from 'react-router-dom'
import { api } from '../../api/client'
import { useApiMutation, useEmails, useJobPostings } from '../../api/hooks'
import { useSession } from '../../auth/sessionContext'
import { EmailModal } from '../../components/EmailModal'
import { useErrorToast, useToast } from '../../components/toastContext'
import { Card, Chip, ErrorBox, Loading } from '../../components/ui'
import type { EmailMessage, JobPosting, PublicApplicationInput, PublicApplicationResult } from '../../domain/types'
import { relativeTime } from '../../utils/format'
import { filterPostings, validateApplication } from './validation'

const NOTICES = [30, 60, 90, 0] as const

const emptyForm = (): PublicApplicationInput => ({
  postingId: '',
  name: '',
  email: '',
  phone: '',
  experienceYears: null,
  currentCtc: null,
  expectedCtc: null,
  noticeDays: 0,
  resumeFileName: '',
  privacyConsent: false,
  coiDeclaration: false,
})

export function CareersPage() {
  const { t } = useTranslation()
  const postings = useJobPostings()
  const toast = useToast()
  const onError = useErrorToast()
  const { user, tenant } = useSession()
  const outlet = useOutletContext<{ openDrawer: () => void } | null>()
  const [query, setQuery] = useState('')
  const [location, setLocation] = useState('')
  const [applied, setApplied] = useState({ query: '', location: '' })
  const [form, setForm] = useState(emptyForm)
  const [result, setResult] = useState<(PublicApplicationResult & { name: string }) | null>(null)
  const formRef = useRef<HTMLDivElement>(null)
  const submit = useApiMutation((v: PublicApplicationInput) => api.submitPublicApplication(v))

  if (postings.isPending) return <Loading />
  if (postings.isError) return <ErrorBox error={postings.error} />

  const visible = filterPostings(postings.data, applied.query, applied.location)
  const locations = [...new Set(postings.data.map((p) => p.locationFilter))]
  const position = postings.data.find((p) => p.id === form.postingId)
  const set = <K extends keyof PublicApplicationInput>(k: K, v: PublicApplicationInput[K]) => setForm((f) => ({ ...f, [k]: v }))
  const num = (s: string) => (s === '' ? null : Number(s))

  const search = () => {
    setApplied({ query, location })
    if (filterPostings(postings.data, query, location).length === 0) toast(t('careers.noResults'), 'warn')
  }

  const choose = (p: JobPosting) => {
    set('postingId', p.id)
    formRef.current?.scrollIntoView?.({ behavior: 'smooth' })
    toast(t('careers.selected', { title: p.title }))
  }

  const onSubmit = () => {
    const problem = validateApplication(form)
    if (problem) return toast(t(problem), 'warn')
    submit.mutate(form, {
      onSuccess: (r) => {
        setResult({ ...r, name: form.name })
        setForm(emptyForm())
        toast(t('careers.success.toast', { appId: r.appId }), 'move')
      },
      onError,
    })
  }

  return (
    <div className="screen">
      <div className="portal-hero">
        <div className="ph-tag">{t('careers.tag', { tenant: tenant.name })}</div>
        <h1>
          <Trans i18nKey="careers.heading" values={{ tagline: tenant.careersTagline }} components={{ hl: <span style={{ color: '#A78BFA' }} /> }} />
        </h1>
        <p>{tenant.careersIntro}</p>
        <div className="search-row">
          <input type="text" aria-label={t('careers.searchLabel')} placeholder={t('careers.search')} value={query} onChange={(e) => setQuery(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && search()} />
          <select aria-label={t('careers.locationLabel')} value={location} onChange={(e) => setLocation(e.target.value)}>
            <option value="">{t('careers.allLocations')}</option>
            {locations.map((l) => <option key={l}>{l}</option>)}
          </select>
          <button type="button" className="btn btn-gold" onClick={search}>{t('careers.searchBtn')}</button>
        </div>
      </div>
      <div>
        {visible.length === 0 && <div className="empty">{t('careers.noResults')}</div>}
        {visible.map((p) => (
          <div key={p.id} className="job-card">
            <div className="jc-main">
              <b>{p.title}</b>
              <div className="jc-meta">
                <span>{p.location}</span><span>·</span><span>{p.experience}</span><span>·</span><span>{p.qualification}</span>
                {p.tags.map((tag) => <Chip key={tag.text} tone={tag.tone}>{tag.text}</Chip>)}
              </div>
            </div>
            <button type="button" className="btn btn-primary" onClick={() => choose(p)}>{t('careers.apply')}</button>
          </div>
        ))}
      </div>

      <div ref={formRef}>
        <Card
          style={{ marginTop: 20 }}
          title={<Trans i18nKey="careers.applyTitle" values={{ position: position?.title ?? t('careers.selectPosition') }} components={{ pos: <span /> }} />}
          aside={<Chip tone="slate">{t('careers.duration')}</Chip>}
        >
          <div style={{ padding: '16px 20px' }}>
            <div className="fgrid">
              <TextField id="name" value={form.name} onChange={(v) => set('name', v)} required />
              <TextField id="email" type="email" value={form.email} onChange={(v) => set('email', v)} required />
              <TextField id="phone" type="tel" value={form.phone} onChange={(v) => set('phone', v)} required />
              <TextField id="experience" type="number" value={form.experienceYears?.toString() ?? ''} onChange={(v) => set('experienceYears', num(v))} />
              <TextField id="currentCtc" type="number" value={form.currentCtc?.toString() ?? ''} onChange={(v) => set('currentCtc', num(v))} />
              <TextField id="expectedCtc" type="number" value={form.expectedCtc?.toString() ?? ''} onChange={(v) => set('expectedCtc', num(v))} />
              <div className="field full" role="radiogroup" aria-label={t('careers.fields.notice')}>
                <label>{t('careers.fields.notice')}</label>
                <div className="pills">
                  {NOTICES.map((n) => (
                    <span key={n}>
                      <input type="radio" name="np" id={`np-${n}`} checked={form.noticeDays === n} onChange={() => set('noticeDays', n)} />
                      <label htmlFor={`np-${n}`}>{t(`careers.notices.${n}`)}</label>
                    </span>
                  ))}
                </div>
              </div>
              <div className="field full">
                <label htmlFor="c-resume">{t('careers.fields.resume')} <span className="req">*</span></label>
                <input id="c-resume" type="file" accept=".pdf,.doc,.docx" onChange={(e) => set('resumeFileName', e.target.files?.[0]?.name ?? '')} />
              </div>
            </div>
            <label className="chk-line" style={{ marginTop: 12 }}>
              <input type="checkbox" checked={form.privacyConsent} onChange={(e) => set('privacyConsent', e.target.checked)} />
              <span><Trans i18nKey="careers.consent" values={{ tenant: tenant.name }} components={{ b: <b /> }} /></span>
            </label>
            <label className="chk-line">
              <input type="checkbox" checked={form.coiDeclaration} onChange={(e) => set('coiDeclaration', e.target.checked)} />
              <span><Trans i18nKey="careers.coi" components={{ b: <b /> }} /></span>
            </label>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 14, flexWrap: 'wrap', gap: 10 }}>
              <span className="subnote">{t('careers.privacyNote')}</span>
              <button type="button" className="btn btn-gold" style={{ padding: '11px 26px' }} disabled={submit.isPending} onClick={onSubmit}>{t('careers.submit')}</button>
            </div>
          </div>
        </Card>
      </div>

      {result && <SuccessCard result={result} />}
      <StatusLookup />
      {user.role === 'candidate' && <MyCommunications onOpenDrawer={outlet?.openDrawer} />}
      <div style={{ marginTop: 22, padding: '14px 18px', background: '#eef2f7', borderRadius: 10, fontSize: 11.5, color: 'var(--muted)' }}>
        <Trans i18nKey="careers.privacyFooter" values={{ tenant: tenant.name }} components={{ b: <b /> }} />
      </div>
    </div>
  )
}

function TextField({ id, value, onChange, type = 'text', required = false }: { id: string; value: string; onChange: (v: string) => void; type?: string; required?: boolean }) {
  const { t } = useTranslation()
  return (
    <div className="field">
      <label htmlFor={`c-${id}`}>{t(`careers.fields.${id}`)} {required && <span className="req">*</span>}</label>
      <input id={`c-${id}`} type={type} min={type === 'number' ? 0 : undefined} step={type === 'number' ? 0.1 : undefined} placeholder={t(`careers.fields.${id}Ph`)} value={value} onChange={(e) => onChange(e.target.value)} />
    </div>
  )
}

function SuccessCard({ result }: { result: PublicApplicationResult & { name: string } }) {
  const { t } = useTranslation()
  const steps = t('careers.success.steps', { returnObjects: true }) as string[]
  return (
    <Card style={{ marginTop: 18 }} title={t('careers.success.title')} aside={<Chip tone="green">{result.appId}</Chip>}>
      <div style={{ padding: '16px 20px' }} role="status">
        <p style={{ fontSize: 13, marginBottom: 6 }}>
          <Trans i18nKey="careers.success.body" values={{ name: result.name, position: result.position }} components={{ b: <b /> }} />
        </p>
        <div className="status-track">
          {steps.map((s, i) => (
            <div key={s} className={`st-step ${i < 2 ? 'on' : ''}`}>
              {s}
              {i === 0 && <><br /><span style={{ fontSize: 9.5 }}>{t('careers.success.today')}</span></>}
              {i === 1 && <><br /><span style={{ fontSize: 9.5 }}>{t('careers.success.review')}</span></>}
            </div>
          ))}
        </div>
      </div>
    </Card>
  )
}

function StatusLookup() {
  const { t } = useTranslation()
  const [id, setId] = useState('')
  const [out, setOut] = useState<{ text: string; tone: 'teal' | 'slate' | 'amber' } | null>(null)
  const check = async () => {
    if (!id.trim()) return setOut({ text: t('careers.status.enter'), tone: 'amber' })
    const status = await api.getApplicationStatus(id)
    setOut(status ? { text: status, tone: 'teal' } : { text: t('careers.status.notFound'), tone: 'slate' })
  }
  return (
    <Card style={{ marginTop: 18 }} title={t('careers.status.check')}>
      <div style={{ display: 'flex', gap: 10, padding: '14px 20px', flexWrap: 'wrap', alignItems: 'center' }}>
        <input className="reason-in" aria-label={t('careers.status.label')} placeholder={t('careers.status.placeholder')} style={{ maxWidth: 280 }} value={id} onChange={(e) => setId(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && void check()} />
        <button type="button" className="btn btn-ghost btn-sm" onClick={() => void check()}>{t('careers.status.check')}</button>
        <span aria-live="polite">{out && <Chip tone={out.tone}>{out.text}</Chip>}</span>
      </div>
    </Card>
  )
}

function MyCommunications({ onOpenDrawer }: { onOpenDrawer?: () => void }) {
  const { t } = useTranslation()
  const emails = useEmails()
  const [open, setOpen] = useState<EmailMessage | null>(null)
  const markRead = useApiMutation(api.markEmailRead)
  return (
    <Card
      style={{ marginTop: 18 }}
      title={t('careers.comms.title')}
      sub={t('careers.comms.sub')}
      aside={onOpenDrawer && <button type="button" className="btn btn-ghost btn-sm" onClick={onOpenDrawer}>{t('careers.comms.open')}</button>}
    >
      <div style={{ padding: '6px 18px 14px' }}>
        {(emails.data ?? []).length === 0 && <p className="subnote">{t('careers.comms.empty')}</p>}
        {(emails.data ?? []).slice(0, 5).map((e) => (
          <button
            key={e.id}
            type="button"
            className="applied-row"
            style={{ width: '100%', background: 'none', border: 'none', borderBottom: '1px dashed #E7ECF2', cursor: 'pointer', textAlign: 'left' }}
            onClick={() => {
              markRead.mutate(e.id)
              setOpen(e)
            }}
          >
            <span style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
              <span style={{ fontSize: 16 }} aria-hidden="true">{e.unread ? '📬' : '📭'}</span>
              <span>
                <b>{e.subject}</b>
                <br />
                <span style={{ fontSize: 11, color: 'var(--muted)' }}>{relativeTime(e.createdAt)} · {e.tag} · {e.from.split('<')[0]?.trim()}</span>
              </span>
            </span>
            <Chip tone={e.unread ? 'amber' : 'slate'}>{e.unread ? t('careers.comms.unread') : t('careers.comms.read')}</Chip>
          </button>
        ))}
      </div>
      {open && <EmailModal email={open} onClose={() => setOpen(null)} />}
    </Card>
  )
}
