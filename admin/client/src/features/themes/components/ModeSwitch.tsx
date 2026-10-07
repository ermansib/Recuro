import { useTranslation } from 'react-i18next'

interface ModeSwitchProps {
  label: string
  dark: boolean
  onChange: (dark: boolean) => void
}

/** Light / dark toggle for previews. */
export function ModeSwitch({ label, dark, onChange }: ModeSwitchProps) {
  const { t } = useTranslation()
  return (
    <div className="segmented" role="group" aria-label={label}>
      <button type="button" aria-pressed={!dark} onClick={() => onChange(false)}>
        {t('common.mode.light')}
      </button>
      <button type="button" aria-pressed={dark} onClick={() => onChange(true)}>
        {t('common.mode.dark')}
      </button>
    </div>
  )
}
