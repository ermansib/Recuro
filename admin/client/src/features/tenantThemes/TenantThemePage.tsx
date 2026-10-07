import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router-dom'
import { useAssignTenantTheme, useTenants, useThemes } from '../../api/hooks'
import { THEME_MODES, type Tenant, type ThemeMode, type ThemePreset } from '../../api/types'
import { ErrorMessage, PageHeader, QueryState, StatusMessage } from '../../components/ui'
import { ModeSwitch } from '../themes/components/ModeSwitch'
import { ThemePicker } from '../themes/components/ThemePicker'
import { ThemePreview } from '../themes/components/ThemePreview'

/**
 * Platform console: pick a tenant, then the theme and default colour mode its portal uses (RCU-PLT-006).
 * Each tenant gets its own theme; the main portal reads it from the runtime config.
 */
export function TenantThemePage() {
  const { t } = useTranslation()
  const tenants = useTenants()
  const themes = useThemes()
  const [params, setParams] = useSearchParams()

  const available = themes.data?.filter((preset) => preset.isPublished) ?? []
  const tenant = tenants.data?.find((candidate) => candidate.id === params.get('tenant')) ?? tenants.data?.[0]

  return (
    <>
      <PageHeader title={t('tenantThemes.title')} subtitle={t('tenantThemes.subtitle')} />
      <QueryState isLoading={tenants.isLoading || themes.isLoading} error={tenants.error ?? themes.error}>
        {tenant ? (
          <>
            <div className="field" style={{ maxWidth: 360, marginBottom: 16 }}>
              <label htmlFor="tenant-select">{t('tenantThemes.tenant')}</label>
              <select
                id="tenant-select"
                className="input"
                value={tenant.id}
                onChange={(event) => setParams({ tenant: event.target.value }, { replace: true })}
              >
                {tenants.data?.map((option) => (
                  <option key={option.id} value={option.id}>
                    {option.name}
                  </option>
                ))}
              </select>
            </div>
            <TenantThemeEditor key={tenant.id} tenant={tenant} presets={available} />
          </>
        ) : (
          <p className="muted">{t('tenants.empty')}</p>
        )}
      </QueryState>
    </>
  )
}

function TenantThemeEditor({ tenant, presets }: { tenant: Tenant; presets: ThemePreset[] }) {
  const { t } = useTranslation()
  const assign = useAssignTenantTheme()
  const [presetKey, setPresetKey] = useState(tenant.themePresetKey)
  const [mode, setMode] = useState<ThemeMode>(tenant.themeMode)
  const [previewDark, setPreviewDark] = useState(tenant.themeMode === 'dark')
  const [saved, setSaved] = useState<string | null>(null)

  const selected = presets.find((preset) => preset.key === presetKey) ?? presets[0]
  const dirty = presetKey !== tenant.themePresetKey || mode !== tenant.themeMode

  const chooseMode = (next: ThemeMode) => {
    setMode(next)
    if (next !== 'system') setPreviewDark(next === 'dark')
  }

  const save = () => {
    setSaved(null)
    assign.mutate(
      { id: tenant.id, request: { themePresetKey: presetKey, themeMode: mode } },
      { onSuccess: () => setSaved(t('tenantThemes.savedToast', { tenant: tenant.name })) },
    )
  }

  return (
    <>
      <ErrorMessage error={assign.error} />
      <StatusMessage message={saved} />
      <div className="picker-layout">
        <section className="card" aria-labelledby="presets-heading">
          <h2 id="presets-heading">{t('tenantThemes.presets', { tenant: tenant.name })}</h2>
          <ThemePicker
            presets={presets}
            selectedKey={presetKey}
            currentKey={tenant.themePresetKey}
            dark={previewDark}
            label={t('tenantThemes.presets', { tenant: tenant.name })}
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
            <button type="button" className="btn" disabled={!dirty || assign.isPending} onClick={save}>
              {assign.isPending ? t('common.saving') : t('tenantThemes.assign', { tenant: tenant.name })}
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
              <ThemePreview palette={previewDark ? selected.dark : selected.light} tenantName={tenant.name} dark={previewDark} />
            </div>
          )}
        </section>
      </div>
    </>
  )
}
