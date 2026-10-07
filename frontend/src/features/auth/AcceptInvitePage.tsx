import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router-dom'
import { api } from '../../api/client'
import { useInvitation } from '../../api/hooks'
import { Loading } from '../../components/ui'
import { unmetPasswordRules } from '../../domain/auth'
import type { InvitationView } from '../../domain/types'
import { AuthShell } from './AuthShell'
import { AuthField, FormError, PasswordChecklist, PasswordField } from './fields'
import { useCompleteSignIn } from './useCompleteSignIn'

/** Staff join a workspace by invitation and set their own password. */
export function AcceptInvitePage() {
  const { t } = useTranslation()
  const { token = '' } = useParams()
  const invitation = useInvitation(token)

  if (invitation.isPending) return <Loading />
  if (invitation.isError) {
    return (
      <AuthShell>
        <h2>{t('auth.invite.invalidTitle')}</h2>
        <FormError error={invitation.error} />
        <Link to="/signin">{t('auth.forgot.back')}</Link>
      </AuthShell>
    )
  }
  return (
    <AuthShell branding={invitation.data.workspace}>
      <AcceptForm token={token} invitation={invitation.data} />
    </AuthShell>
  )
}

function AcceptForm({ token, invitation }: { token: string; invitation: InvitationView }) {
  const { t } = useTranslation()
  const complete = useCompleteSignIn()
  const [name, setName] = useState(invitation.name)
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<{ name?: string; password?: string }>({})
  const [failure, setFailure] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    const next = {
      name: name.trim() ? undefined : t('auth.errors.name'),
      password: unmetPasswordRules(password).length === 0 ? undefined : t('auth.errors.passwordPolicy'),
    }
    setErrors(next)
    if (next.name || next.password) return
    setBusy(true)
    setFailure(null)
    try {
      complete(await api.acceptInvitation({ token, name, password }))
    } catch (err) {
      setFailure(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={(e) => void submit(e)} noValidate>
      <h2>{t('auth.invite.title', { workspace: invitation.workspace.name })}</h2>
      <p className="auth-sub">
        {t('auth.invite.body', { invitedBy: invitation.invitedBy, role: t(`common.roles.${invitation.role}`) })}
      </p>
      <FormError error={failure} />
      <AuthField label={t('auth.fields.email')} type="email" value={invitation.email} onChange={() => undefined} readOnly autoComplete="username" />
      <AuthField label={t('auth.fields.name')} value={name} onChange={setName} error={errors.name} autoComplete="name" />
      <PasswordField label={t('auth.fields.password')} value={password} onChange={setPassword} error={errors.password} autoComplete="new-password" autoFocus />
      <PasswordChecklist password={password} />
      <button type="submit" className="btn btn-gold auth-submit" disabled={busy}>
        {busy ? t('auth.invite.submitting') : t('auth.invite.submit')}
      </button>
    </form>
  )
}
