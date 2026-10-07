import { useRef, type KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { ThemePreset } from '../../../api/types'
import { Swatches } from './Swatches'

interface ThemePickerProps {
  presets: ThemePreset[]
  selectedKey: string
  currentKey?: string
  dark: boolean
  label: string
  onSelect: (key: string) => void
}

const NEXT_KEYS = new Set(['ArrowRight', 'ArrowDown'])
const PREVIOUS_KEYS = new Set(['ArrowLeft', 'ArrowUp'])

/** Gallery of presets as a radio group: click, or use arrow keys, to pick one. */
export function ThemePicker({ presets, selectedKey, currentKey, dark, label, onSelect }: ThemePickerProps) {
  const { t } = useTranslation()
  const cards = useRef<(HTMLButtonElement | null)[]>([])
  const selectedIndex = Math.max(
    0,
    presets.findIndex((preset) => preset.key === selectedKey),
  )

  const handleKeyDown = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    const step = NEXT_KEYS.has(event.key) ? 1 : PREVIOUS_KEYS.has(event.key) ? -1 : 0
    if (step === 0) return
    event.preventDefault()
    const nextIndex = (index + step + presets.length) % presets.length
    const next = presets[nextIndex]
    if (!next) return
    onSelect(next.key)
    cards.current[nextIndex]?.focus()
  }

  return (
    <div className="theme-grid" role="radiogroup" aria-label={label}>
      {presets.map((preset, index) => {
        const palette = dark ? preset.dark : preset.light
        const checked = preset.key === selectedKey
        return (
          <button
            key={preset.key}
            ref={(element) => {
              cards.current[index] = element
            }}
            type="button"
            role="radio"
            aria-checked={checked}
            tabIndex={index === selectedIndex ? 0 : -1}
            className="theme-card"
            onClick={() => onSelect(preset.key)}
            onKeyDown={(event) => handleKeyDown(event, index)}
          >
            {preset.key === currentKey && <span className="badge neutral tag">{t('branding.current')}</span>}
            <Swatches palette={palette} />
            <div className="theme-name">{preset.name}</div>
            <div className="hex">
              {palette.primary} {palette.secondary} {palette.accent}
            </div>
          </button>
        )
      })}
    </div>
  )
}
