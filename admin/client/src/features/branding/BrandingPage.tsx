import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useBranding, useUpdateBranding } from '../../api/hooks'
import { THEME_MODES, type Branding, type ThemeMode } from '../../api/types'
import { ErrorMessage, PageHeader, QueryState, StatusMessage } from '../../components/ui'
import { ModeSwitch } from '../themes/components/ModeSwitch'
import { ThemePicker } from '../themes/components/ThemePicker'
import { ThemePreview } from '../themes/components/ThemePreview'

/** Tenant admin: pick a theme preset and default mode, with a live preview (RCU-PLT-006). */
export function BrandingPage() {
  const branding = useBranding()
  return (
    <QueryState isLoading={branding.isLoading} error={branding.error}>
      {branding.data && <BrandingEditor branding={branding.data} />}
    </QueryState>
  )
}

function BrandingEditor({ branding }: { branding: Branding }) {
  const { t } = useTranslation()
  const update = useUpdateBranding()
  const [presetKey, setPresetKey] = useState(branding.themePresetKey)
  const [mode, setMode] = useState<ThemeMode>(branding.themeMode)
  const [previewDark, setPreviewDark] = useState(branding.themeMode === 'dark')
  const [saved, setSaved] = useState<string | null>(null)

  const selected = branding.presets.find((preset) => preset.key === presetKey) ?? branding.presets[0]
  const dirty = presetKey !== branding.themePresetKey || mode !== branding.themeMode

  const chooseMode = (next: ThemeMode) => {
    setMode(next)
    if (next !== 'system') setPreviewDark(next === 'dark')
  }

  const save = () => {
    setSaved(null)
    update.mutate({ themePresetKey: presetKey, themeMode: mode }, { onSuccess: () => setSaved(t('branding.savedToast')) })
  }

  return (
    <>
      <PageHeader title={t('branding.title')} subtitle={t('branding.subtitle', { tenant: branding.tenantName })} />
      <ErrorMessage error={update.error} />
      <StatusMessage message={saved} />
      <div className="picker-layout">
        <section className="card" aria-labelledby="presets-heading">
          <h2 id="presets-heading">{t('branding.presets')}</h2>
          <ThemePicker
            presets={branding.presets}
            selectedKey={presetKey}
            currentKey={branding.themePresetKey}
            dark={previewDark}
            label={t('branding.presets')}
            onSelect={setPresetKey}
          />
          <h2 id="mode-heading" style={{ marginTop: 20 }}>
            {t('branding.mode')}
          </h2>
          <div className="segmented" role="radiogroup" aria-labelledby="mode-heading">
            {THEME_MODES.map((option) => (
              <button key={option} type="button" role="radio" aria-checked={mode === option} onClick={() => chooseMode(option)}>
                {t(`common.mode.${option}`)}
              </button>
            ))}
          </div>
          <p className="muted">{t('branding.modeHint')}</p>
          <div className="actions">
            <button type="button" className="btn" disabled={!dirty || update.isPending} onClick={save}>
              {update.isPending ? t('common.saving') : t('common.save')}
            </button>
            {dirty && <span className="muted">{t('branding.unsaved')}</span>}
          </div>
        </section>
        <section className="card" aria-labelledby="preview-heading">
          <div className="actions" style={{ marginTop: 0, justifyContent: 'space-between' }}>
            <h2 id="preview-heading" style={{ margin: 0 }}>
              {t('branding.preview')}
            </h2>
            <ModeSwitch label={t('branding.previewMode')} dark={previewDark} onChange={setPreviewDark} />
          </div>
          {selected && (
            <div style={{ marginTop: 14 }}>
              <ThemePreview palette={previewDark ? selected.dark : selected.light} tenantName={branding.tenantName} dark={previewDark} />
            </div>
          )}
        </section>
      </div>
    </>
  )
}
