import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useBranding } from '../../api/hooks'
import type { Branding } from '../../api/types'
import { PageHeader, QueryState } from '../../components/ui'
import { ModeSwitch } from '../themes/components/ModeSwitch'
import { Swatches } from '../themes/components/Swatches'
import { ThemePreview } from '../themes/components/ThemePreview'

/** Tenant admin: the theme the platform console assigned to this tenant. View only (RCU-PLT-006). */
export function BrandingPage() {
  const branding = useBranding()
  return (
    <QueryState isLoading={branding.isLoading} error={branding.error}>
      {branding.data && <BrandingView branding={branding.data} />}
    </QueryState>
  )
}

function BrandingView({ branding }: { branding: Branding }) {
  const { t } = useTranslation()
  const [previewDark, setPreviewDark] = useState(branding.themeMode === 'dark')
  const { theme } = branding

  return (
    <>
      <PageHeader title={t('branding.title')} subtitle={t('branding.subtitle', { tenant: branding.tenantName })} />
      <div className="picker-layout">
        <section className="card" aria-labelledby="assigned-heading">
          <h2 id="assigned-heading">{t('branding.assigned')}</h2>
          <dl className="facts">
            <dt>{t('branding.theme')}</dt>
            <dd>{theme.name}</dd>
            <dt>{t('branding.mode')}</dt>
            <dd>{t(`common.mode.${branding.themeMode}`)}</dd>
          </dl>
          <Swatches palette={previewDark ? theme.dark : theme.light} />
          <p className="muted">{t('branding.managedHint')}</p>
        </section>
        <section className="card" aria-labelledby="preview-heading">
          <div className="actions" style={{ marginTop: 0, justifyContent: 'space-between' }}>
            <h2 id="preview-heading" style={{ margin: 0 }}>
              {t('branding.preview')}
            </h2>
            <ModeSwitch label={t('branding.previewMode')} dark={previewDark} onChange={setPreviewDark} />
          </div>
          <div style={{ marginTop: 14 }}>
            <ThemePreview palette={previewDark ? theme.dark : theme.light} tenantName={branding.tenantName} dark={previewDark} />
          </div>
        </section>
      </div>
    </>
  )
}
