import { unmetPasswordRules } from '@workspace-form'
import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useResetMemberPassword } from '../../api/hooks'
import type { RoleScope, WorkspaceMember } from '../../api/types'
import { ErrorMessage } from '../../components/ui'
import { TemporaryPasswordField } from './TemporaryPasswordField'

interface ResetPasswordFormProps {
  scope: RoleScope
  member: WorkspaceMember
  onDone: (password: string) => void
  onCancel: () => void
}

/** Sets a temporary password for someone who forgot theirs; the admin gives it to them directly. */
export function ResetPasswordForm({ scope, member, onDone, onCancel }: ResetPasswordFormProps) {
  const { t } = useTranslation()
  const id = useId()
  const reset = useResetMemberPassword(scope)
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | undefined>()

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (unmetPasswordRules(password).length > 0) {
      setError('roles.members.errors.password')
      return
    }
    setError(undefined)
    reset.mutate({ accountId: member.accountId, request: { temporaryPassword: password } }, { onSuccess: () => onDone(password) })
  }

  return (
    <form className="member-form" onSubmit={submit} noValidate aria-label={t('roles.members.resetPasswordFor', { name: member.name })}>
      <p className="muted">{t('roles.members.resetIntro', { name: member.name })}</p>
      <ErrorMessage error={reset.error} />
      <TemporaryPasswordField id={`${id}-password`} value={password} onChange={setPassword} error={error} />
      <div className="actions">
        <button type="submit" className="btn small" disabled={reset.isPending}>
          {reset.isPending ? t('common.saving') : t('roles.members.setPassword')}
        </button>
        <button type="button" className="btn secondary small" onClick={onCancel}>
          {t('common.cancel')}
        </button>
      </div>
    </form>
  )
}
