import { PASSWORD_RULE_ORDER, WORKSPACE_FORM_LIMITS, unmetPasswordRules } from '@workspace-form'
import { useTranslation } from 'react-i18next'
import { suggestTemporaryPassword } from './memberDraft'

interface TemporaryPasswordFieldProps {
  id: string
  value: string
  onChange: (value: string) => void
  error?: string
}

/**
 * A temporary password the admin chooses (or has suggested) and gives to the person themselves. It is shown in
 * plain text so it can be copied; the person must replace it at their next sign-in.
 */
export function TemporaryPasswordField({ id, value, onChange, error }: TemporaryPasswordFieldProps) {
  const { t } = useTranslation()
  const unmet = new Set(unmetPasswordRules(value))
  const errorId = `${id}-err`

  return (
    <div className="field">
      <label htmlFor={id}>{t('roles.members.temporaryPassword')}</label>
      <div className="actions" style={{ marginTop: 0 }}>
        <input
          id={id}
          type="text"
          autoComplete="off"
          spellCheck={false}
          value={value}
          onChange={(event) => onChange(event.target.value)}
          aria-invalid={!!error}
          aria-describedby={error ? errorId : undefined}
        />
        <button type="button" className="btn secondary small" onClick={() => onChange(suggestTemporaryPassword())}>
          {t('roles.members.suggestPassword')}
        </button>
      </div>
      <small>{t('roles.members.temporaryPasswordHint')}</small>
      <small>{t('workspaceForm.passwordRules.title')}</small>
      <ul className="rules">
        {PASSWORD_RULE_ORDER.map((rule) => (
          <li key={rule} className={unmet.has(rule) ? '' : 'ok'}>
            <span aria-hidden="true">{unmet.has(rule) ? '○' : '✓'}</span>{' '}
            {t(`workspaceForm.passwordRules.${rule}`, WORKSPACE_FORM_LIMITS.password)}
          </li>
        ))}
      </ul>
      {error && (
        <small id={errorId} className="err-text" role="alert">
          {t(error)}
        </small>
      )}
    </div>
  )
}
