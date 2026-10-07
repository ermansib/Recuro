import { useId, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { ThemeMode } from '../domain/types'
import { useTheme } from '../theme/themeContext'

const icon = (path: ReactNode) => (
  <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
    {path}
  </svg>
)

const OPTIONS: { mode: ThemeMode; icon: ReactNode }[] = [
  {
    mode: 'light',
    icon: icon(<><circle cx="12" cy="12" r="4" /><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" /></>),
  },
  { mode: 'system', icon: icon(<><rect x="3" y="4" width="18" height="12" rx="2" /><path d="M8 20h8M12 16v4" /></>) },
  { mode: 'dark', icon: icon(<path d="M20 14.5A8 8 0 0 1 9.5 4 8 8 0 1 0 20 14.5Z" />) },
]

/** Light / match device / dark. Native radios, so arrow keys and screen readers work as expected. */
export function ThemeSwitch({ className = '' }: { className?: string }) {
  const { t } = useTranslation()
  const { mode, setMode } = useTheme()
  const name = useId()

  return (
    <fieldset className={`theme-sw ${className}`}>
      <legend className="sr-only">{t('common.appearance.label')}</legend>
      {OPTIONS.map((o) => (
        <label key={o.mode} title={t(`common.appearance.${o.mode}`)} className={mode === o.mode ? 'on' : ''}>
          <input
            type="radio"
            name={name}
            value={o.mode}
            checked={mode === o.mode}
            onChange={() => setMode(o.mode)}
            aria-label={t(`common.appearance.${o.mode}`)}
          />
          {o.icon}
        </label>
      ))}
    </fieldset>
  )
}
