import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router-dom'
import { api } from '../../api/client'
import { useAuth } from '../../auth/sessionContext'
import { unmetPasswordRules } from '../../domain/auth'
import { AuthShell } from './AuthShell'
import { FormError, PasswordChecklist, PasswordField } from './fields'

export function ResetPasswordPage() {
  const { t } = useTranslation()
  const [params] = useSearchParams()
  const token = params.get('token') ?? ''
  const { session, signOut } = useAuth()
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [errors, setErrors] = useState<{ password?: string; confirm?: string }>({})
  const [failure, setFailure] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    const next = {
      password: unmetPasswordRules(password).length === 0 ? undefined : t('auth.errors.passwordPolicy'),
      confirm: password === confirm ? undefined : t('auth.errors.mismatch'),
    }
    setErrors(next)
    if (next.password || next.confirm) return
    setBusy(true)
    setFailure(null)
    try {
      await api.resetPassword(token, password)
      // The service ends every session for the account; drop ours too if this browser had one.
      if (session) await signOut('user')
      setDone(true)
    } catch (err) {
      setFailure(err)
    } finally {
      setBusy(false)
    }
  }

  if (!token) {
    return (
      <AuthShell>
        <h2>{t('auth.reset.title')}</h2>
        <p className="auth-sub">{t('auth.reset.missingToken')}</p>
        <Link to="/forgot-password">{t('auth.reset.requestNew')}</Link>
      </AuthShell>
    )
  }

  if (done) {
    return (
      <AuthShell>
        <div role="status">
          <h2>{t('auth.reset.doneTitle')}</h2>
          <p className="auth-sub">{t('auth.reset.doneBody')}</p>
          <Link className="btn btn-primary auth-submit" to="/signin">{t('auth.signIn.submit')}</Link>
        </div>
      </AuthShell>
    )
  }

  return (
    <AuthShell>
      <form onSubmit={(e) => void submit(e)} noValidate>
        <h2>{t('auth.reset.title')}</h2>
        <FormError error={failure} />
        <PasswordField label={t('auth.fields.newPassword')} value={password} onChange={setPassword} error={errors.password} autoComplete="new-password" autoFocus />
        <PasswordChecklist password={password} />
        <PasswordField label={t('auth.fields.confirmPassword')} value={confirm} onChange={setConfirm} error={errors.confirm} autoComplete="new-password" />
        <button type="submit" className="btn btn-primary auth-submit" disabled={busy}>
          {busy ? t('auth.reset.submitting') : t('auth.reset.submit')}
        </button>
        {failure !== null && <Link to="/forgot-password">{t('auth.reset.requestNew')}</Link>}
      </form>
    </AuthShell>
  )
}
