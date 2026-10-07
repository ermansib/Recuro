import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { api } from '../../api/client'
import { useWorkspaceBranding } from '../../api/hooks'
import { Loading } from '../../components/ui'
import { isValidEmail, unmetPasswordRules } from '../../domain/auth'
import type { WorkspaceBranding } from '../../domain/types'
import { AuthShell } from './AuthShell'
import { AuthField, FormError, PasswordChecklist, PasswordField } from './fields'
import { useCompleteSignIn } from './useCompleteSignIn'
import { useWorkspaceParam, withWorkspace } from './useWorkspace'

/** Candidates create their own account on a tenant's careers site (RCU-CAR). */
export function CandidateSignUpPage() {
  const { t } = useTranslation()
  const workspace = useWorkspaceParam()
  const branding = useWorkspaceBranding(workspace)

  if (!workspace || branding.isError) {
    return (
      <AuthShell>
        <h2>{t('auth.candidate.title')}</h2>
        <FormError error={branding.error} />
        <p className="auth-sub">{t('auth.candidate.missingWorkspace')}</p>
        <Link to="/signin">{t('auth.forgot.back')}</Link>
      </AuthShell>
    )
  }
  if (branding.isPending) return <Loading />
  return (
    <AuthShell branding={branding.data}>
      <CandidateForm branding={branding.data} />
    </AuthShell>
  )
}

type Errors = Partial<Record<'name' | 'email' | 'password' | 'consent', string>>

function CandidateForm({ branding }: { branding: WorkspaceBranding }) {
  const { t } = useTranslation()
  const complete = useCompleteSignIn()
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [consent, setConsent] = useState(false)
  const [errors, setErrors] = useState<Errors>({})
  const [failure, setFailure] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    const next: Errors = {
      name: name.trim() ? undefined : t('auth.errors.name'),
      email: isValidEmail(email) ? undefined : t('auth.errors.email'),
      password: unmetPasswordRules(password).length === 0 ? undefined : t('auth.errors.passwordPolicy'),
      consent: consent ? undefined : t('auth.errors.consent'),
    }
    setErrors(next)
    if (Object.values(next).some(Boolean)) return
    setBusy(true)
    setFailure(null)
    try {
      complete(await api.registerCandidate({ workspace: branding.slug, name, email, password, privacyConsent: consent }))
    } catch (err) {
      setFailure(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={(e) => void submit(e)} noValidate>
      <h2>{t('auth.candidate.title')}</h2>
      <p className="auth-sub">{t('auth.candidate.subtitle', { workspace: branding.name })}</p>
      <FormError error={failure} />
      <AuthField label={t('auth.fields.name')} value={name} onChange={setName} error={errors.name} autoComplete="name" autoFocus />
      <AuthField label={t('auth.fields.email')} type="email" value={email} onChange={setEmail} error={errors.email} autoComplete="email" />
      <PasswordField label={t('auth.fields.password')} value={password} onChange={setPassword} error={errors.password} autoComplete="new-password" />
      <PasswordChecklist password={password} />
      <label className="chk-line">
        <input type="checkbox" checked={consent} onChange={(e) => setConsent(e.target.checked)} aria-invalid={!!errors.consent} />
        <span>{t('auth.candidate.consent', { workspace: branding.name })}</span>
      </label>
      {errors.consent && <span className="err-text" role="alert">{errors.consent}</span>}
      <button type="submit" className="btn btn-gold auth-submit" disabled={busy}>
        {busy ? t('auth.candidate.submitting') : t('auth.candidate.submit')}
      </button>
      <p className="auth-alt">
        {t('auth.signUp.haveAccount')} <Link to={withWorkspace('/signin', branding.slug)}>{t('auth.signUp.signInLink')}</Link>
      </p>
    </form>
  )
}
