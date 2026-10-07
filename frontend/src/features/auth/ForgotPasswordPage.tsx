import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { api } from '../../api/client'
import { useWorkspaceBranding } from '../../api/hooks'
import { Loading } from '../../components/ui'
import { features } from '../../config/features'
import { RESET_LINK_VALID_MINUTES, isValidEmail } from '../../domain/auth'
import type { PasswordResetRequested, WorkspaceBranding } from '../../domain/types'
import { AuthShell } from './AuthShell'
import { AuthField, FormError } from './fields'
import { useWorkspaceParam, withWorkspace } from './useWorkspace'

export function ForgotPasswordPage() {
  const workspace = useWorkspaceParam()
  const branding = useWorkspaceBranding(workspace)
  if (workspace && branding.isPending) return <Loading />
  if (!branding.data) {
    // Without a workspace we can't know which account to reset; start from the sign-in page.
    return <AuthShell><MissingWorkspace /></AuthShell>
  }
  return (
    <AuthShell branding={branding.data}>
      <ForgotForm branding={branding.data} />
    </AuthShell>
  )
}

function MissingWorkspace() {
  const { t } = useTranslation()
  return (
    <>
      <h2>{t('auth.forgot.title')}</h2>
      <p className="auth-sub">{t('auth.signIn.chooseWorkspace')}</p>
      <Link to="/signin?workspace=">{t('auth.forgot.back')}</Link>
    </>
  )
}

function ForgotForm({ branding }: { branding: WorkspaceBranding }) {
  const { t } = useTranslation()
  const [email, setEmail] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [failure, setFailure] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  const [sent, setSent] = useState<PasswordResetRequested | null>(null)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!isValidEmail(email)) return setError(t('auth.errors.email'))
    setError(null)
    setBusy(true)
    setFailure(null)
    try {
      setSent(await api.requestPasswordReset(branding.slug, email))
    } catch (err) {
      setFailure(err)
    } finally {
      setBusy(false)
    }
  }

  const back = <Link to={withWorkspace('/signin', branding.slug)}>{t('auth.forgot.back')}</Link>

  if (sent) {
    return (
      <div role="status">
        <h2>{t('auth.forgot.sentTitle')}</h2>
        <p className="auth-sub">
          {t('auth.forgot.sentBody', { to: sent.deliveredTo, workspace: branding.name, minutes: RESET_LINK_VALID_MINUTES })}
        </p>
        {features.demoPersonas && sent.demoResetPath && (
          <p className="auth-info">
            <Link to={sent.demoResetPath}>{t('auth.forgot.demoLink')}</Link>
          </p>
        )}
        {back}
      </div>
    )
  }

  return (
    <form onSubmit={(e) => void submit(e)} noValidate>
      <h2>{t('auth.forgot.title')}</h2>
      <p className="auth-sub">{t('auth.forgot.body')}</p>
      <FormError error={failure} />
      <AuthField label={t('auth.fields.email')} type="email" value={email} onChange={setEmail} error={error} autoComplete="email" autoFocus />
      <button type="submit" className="btn btn-primary auth-submit" disabled={busy}>
        {busy ? t('auth.forgot.submitting') : t('auth.forgot.submit')}
      </button>
      <p className="auth-alt">{back}</p>
    </form>
  )
}
