import { PASSWORD_RULE_ORDER, WORKSPACE_FORM_LIMITS, unmetPasswordRules } from '@workspace-form'
import { useTranslation } from 'react-i18next'
import { suggestTemporaryPassword } from '../utils/password'

interface TemporaryPasswordFieldProps {
  id: string
  value: string
  onChange: (value: string) => void
  /** Who receives the password and what happens next, already translated. */
  hint: string
  /** An i18n key, shown once the form has been checked. */
  error?: string
}

/**
 * A temporary password the admin chooses (or has suggested) and gives to the person themselves. It is shown in
 * plain text so it can be copied; the person must replace it at their next sign-in.
 */
export function TemporaryPasswordField({ id, value, onChange, hint, error }: TemporaryPasswordFieldProps) {
  const { t } = useTranslation()
  const unmet = new Set(unmetPasswordRules(value))
  const errorId = `${id}-err`
  const rulesId = `${id}-rules`

  return (
    <div className="field">
      <label htmlFor={id}>{t('common.temporaryPassword')}</label>
      <div className="input-row">
        <input
          id={id}
          type="text"
          className="mono"
          autoComplete="off"
          spellCheck={false}
          value={value}
          onChange={(event) => onChange(event.target.value)}
          aria-invalid={!!error}
          aria-describedby={error ? `${errorId} ${rulesId}` : rulesId}
        />
        <button type="button" className="btn secondary" onClick={() => onChange(suggestTemporaryPassword())}>
          {t('common.suggestPassword')}
        </button>
      </div>
      <small>{hint}</small>
      {error && (
        <small id={errorId} className="err-text" role="alert">
          {t(error)}
        </small>
      )}
      <div id={rulesId} className="rules-box">
        <small>{t('workspaceForm.passwordRules.title')}</small>
        <ul className="rules">
          {PASSWORD_RULE_ORDER.map((rule) => {
            const met = !unmet.has(rule)
            return (
              <li key={rule} className={met ? 'ok' : ''}>
                <span className="rule-mark" aria-hidden="true">
                  {met ? '✓' : ''}
                </span>
                {t(`workspaceForm.passwordRules.${rule}`, WORKSPACE_FORM_LIMITS.password)}
                <span className="sr-only">{met ? t('common.ruleMet') : t('common.ruleUnmet')}</span>
              </li>
            )
          })}
        </ul>
      </div>
    </div>
  )
}
