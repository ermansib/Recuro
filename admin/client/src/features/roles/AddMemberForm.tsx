import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useCreateMember } from '../../api/hooks'
import type { RoleScope, WorkspaceMember, WorkspaceRole } from '../../api/types'
import { ErrorMessage } from '../../components/ui'
import { EMPTY_MEMBER, MEMBER_LIMITS, toCreateRequest, validateMember, type MemberDraft, type MemberField } from './memberDraft'
import { toggle } from './roleDraft'
import { TemporaryPasswordField } from './TemporaryPasswordField'

interface AddMemberFormProps {
  scope: RoleScope
  roles: WorkspaceRole[]
  onAdded: (member: WorkspaceMember) => void
  onCancel: () => void
}

/** Adds a person to the workspace with their roles and a temporary password (email isn't set up yet). */
export function AddMemberForm({ scope, roles, onAdded, onCancel }: AddMemberFormProps) {
  const { t } = useTranslation()
  const ids = useId()
  const create = useCreateMember(scope)
  const [draft, setDraft] = useState<MemberDraft>(EMPTY_MEMBER)
  const [errors, setErrors] = useState<Partial<Record<MemberField, string>>>({})
  const activeRoles = roles.filter((role) => role.isActive)

  const id = (field: string) => `${ids}-${field}`
  const update = (patch: Partial<MemberDraft>) => setDraft((current) => ({ ...current, ...patch }))
  const invalid = (field: MemberField) => ({
    'aria-invalid': !!errors[field],
    'aria-describedby': errors[field] ? id(`${field}-err`) : undefined,
  })
  const errorOf = (field: MemberField) =>
    errors[field] ? (
      <small id={id(`${field}-err`)} className="err-text" role="alert">
        {t(errors[field], { max: MEMBER_LIMITS.name })}
      </small>
    ) : null

  const submit = (event: FormEvent) => {
    event.preventDefault()
    const next = validateMember(draft)
    setErrors(next)
    if (Object.values(next).some(Boolean)) return
    create.mutate(toCreateRequest(draft), { onSuccess: onAdded })
  }

  return (
    <form className="member-form" onSubmit={submit} noValidate aria-labelledby={id('title')}>
      <h3 id={id('title')} className="form-section">
        {t('roles.members.addTitle')}
      </h3>
      <p className="muted">{t('roles.members.addIntro')}</p>
      <ErrorMessage error={create.error} />
      <div className="form-grid">
        <div className="field">
          <label htmlFor={id('name')}>{t('roles.members.fullName')}</label>
          <input id={id('name')} maxLength={MEMBER_LIMITS.name} value={draft.name} onChange={(event) => update({ name: event.target.value })} {...invalid('name')} />
          {errorOf('name')}
        </div>
        <div className="field">
          <label htmlFor={id('email')}>{t('roles.members.email')}</label>
          <input id={id('email')} type="email" value={draft.email} onChange={(event) => update({ email: event.target.value })} {...invalid('email')} />
          {errorOf('email')}
        </div>
        <div className="field">
          <label htmlFor={id('department')}>{t('roles.members.department')}</label>
          <input
            id={id('department')}
            maxLength={MEMBER_LIMITS.department}
            value={draft.department}
            onChange={(event) => update({ department: event.target.value })}
          />
        </div>
      </div>
      <fieldset className="role-options" {...invalid('roleKeys')}>
        <legend>{t('roles.members.roles')}</legend>
        {activeRoles.map((role) => (
          <label key={role.key} className="check">
            <input
              type="checkbox"
              checked={draft.roleKeys.includes(role.key)}
              onChange={(event) => update({ roleKeys: toggle(draft.roleKeys, role.key, event.target.checked) })}
            />
            {role.name}
          </label>
        ))}
        {errorOf('roleKeys')}
      </fieldset>
      <TemporaryPasswordField
        id={id('password')}
        value={draft.temporaryPassword}
        onChange={(temporaryPassword) => update({ temporaryPassword })}
        error={errors.temporaryPassword}
      />
      <div className="actions">
        <button type="submit" className="btn" disabled={create.isPending}>
          {create.isPending ? t('common.saving') : t('roles.members.create')}
        </button>
        <button type="button" className="btn secondary" onClick={onCancel}>
          {t('common.cancel')}
        </button>
      </div>
    </form>
  )
}
