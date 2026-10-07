import { useTranslation } from 'react-i18next'
import type { ThemePalette } from '../../../api/types'
import { readableTextOn } from '../../../utils/color'

interface ThemePreviewProps {
  palette: ThemePalette
  tenantName: string
  dark: boolean
}

const DARK_DIVIDER = 'rgba(255,255,255,.1)'
const LIGHT_DIVIDER = '#E2E8F0'

/** A miniature of the main portal painted with a palette, so admins see the result before saving. */
export function ThemePreview({ palette, tenantName, dark }: ThemePreviewProps) {
  const { t } = useTranslation()
  const sidebar = dark ? palette.surface : palette.primary
  const sidebarText = readableTextOn(sidebar)

  return (
    <figure className="preview" style={{ background: palette.background, color: palette.text, margin: 0 }} aria-label={t('branding.preview')}>
      <div className="pv-side" style={{ background: sidebar, color: sidebarText }}>
        <div style={{ fontWeight: 700 }}>
          {tenantName} {t('branding.previewSample.brand')}
        </div>
        <div style={{ background: palette.secondary, color: readableTextOn(palette.secondary) }}>
          {t('branding.previewSample.dashboard')}
        </div>
        <div>{t('branding.previewSample.requisitions')}</div>
        <div>{t('branding.previewSample.pipeline')}</div>
        <div>{t('branding.previewSample.offers')}</div>
      </div>
      <div>
        <div className="pv-top" style={{ background: palette.surface, borderBottom: `1px solid ${dark ? DARK_DIVIDER : LIGHT_DIVIDER}` }}>
          <span>{t('branding.previewSample.dashboard')}</span>
          <span className="pv-chip" style={{ background: palette.accent, color: readableTextOn(palette.accent) }}>
            {t('branding.previewSample.alerts')}
          </span>
        </div>
        <div className="pv-body">
          <div className="pv-kpi" style={{ background: palette.surface }}>
            <div style={{ fontSize: 12, opacity: 0.75 }}>{t('branding.previewSample.kpi')}</div>
            <div style={{ fontSize: 22, fontWeight: 700, color: palette.primary }}>24</div>
          </div>
          <span className="pv-btn" style={{ background: palette.primary, color: readableTextOn(palette.primary) }}>
            {t('branding.previewSample.primaryAction')}
          </span>
          <span className="pv-btn" style={{ border: `1px solid ${palette.primary}`, color: palette.primary }}>
            {t('branding.previewSample.secondaryAction')}
          </span>
          <span className="pv-chip" style={{ background: palette.secondary, color: readableTextOn(palette.secondary) }}>
            {t('branding.previewSample.chip')}
          </span>
        </div>
      </div>
    </figure>
  )
}
