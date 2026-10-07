import { useId, useState, type InputHTMLAttributes } from 'react'
import { useTranslation } from 'react-i18next'
import { PASSWORD_MIN_LENGTH, PASSWORD_RULE_ORDER, unmetPasswordRules } from '../../domain/auth'

interface AuthFieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'onChange' | 'value'> {
  label: string
  value: string
  onChange: (value: string) => void
  error?: string | null
  hint?: string
}

/** Labelled input with an inline, announced error (WCAG 2.1 AA, NFR-06). */
export function AuthField({ label, value, onChange, error, hint, id, type = 'text', ...input }: AuthFieldProps) {
  const fallbackId = useId()
  const inputId = id ?? fallbackId
  const describedBy = [error && `${inputId}-err`, hint && `${inputId}-hint`].filter(Boolean).join(' ') || undefined
  return (
    <div className={`field ${error ? 'err' : ''}`}>
      <label htmlFor={inputId}>{label}</label>
      <input
        id={inputId}
        type={type}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        aria-invalid={!!error}
        aria-describedby={describedBy}
        {...input}
      />
      {hint && <span id={`${inputId}-hint`} className="hint">{hint}</span>}
      {error && <span id={`${inputId}-err`} className="err-text" role="alert">{error}</span>}
    </div>
  )
}

export function PasswordField(props: Omit<AuthFieldProps, 'type'>) {
  const { t } = useTranslation()
  const [visible, setVisible] = useState(false)
  return (
    <div className="pw-wrap">
      <AuthField {...props} type={visible ? 'text' : 'password'} />
      <button
        type="button"
        className="pw-toggle"
        aria-pressed={visible}
        aria-label={visible ? t('auth.hidePassword') : t('auth.showPassword')}
        onClick={() => setVisible((v) => !v)}
      >
        {visible ? '🙈' : '👁'}
      </button>
    </div>
  )
}

/** Live checklist of the password policy, so people don't have to guess the rules. */
export function PasswordChecklist({ password }: { password: string }) {
  const { t } = useTranslation()
  const unmet = new Set(unmetPasswordRules(password))
  return (
    <div className="pw-rules">
      <span>{t('auth.passwordRules.title')}</span>
      <ul>
        {PASSWORD_RULE_ORDER.map((rule) => {
          const ok = !unmet.has(rule)
          return (
            <li key={rule} className={ok ? 'ok' : ''}>
              <span aria-hidden="true">{ok ? '✓' : '○'}</span> {t(`auth.passwordRules.${rule}`, { count: PASSWORD_MIN_LENGTH })}
              <span className="sr-only"> ({ok ? t('auth.met') : t('auth.notMet')})</span>
            </li>
          )
        })}
      </ul>
    </div>
  )
}

export function FormError({ error }: { error: unknown }) {
  if (!error) return null
  return (
    <div className="auth-error" role="alert">
      {error instanceof Error ? error.message : String(error)}
    </div>
  )
}
