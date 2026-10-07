import { useTranslation } from 'react-i18next'
import type { ThemePalette } from '../../../api/types'

/** The three brand colours of a palette as a strip. */
export function Swatches({ palette }: { palette: ThemePalette }) {
  const { t } = useTranslation()
  return (
    <div className="swatches">
      <span style={{ background: palette.primary }} title={`${t('themes.swatch.primary')} ${palette.primary}`} />
      <span style={{ background: palette.secondary }} title={`${t('themes.swatch.secondary')} ${palette.secondary}`} />
      <span style={{ background: palette.accent }} title={`${t('themes.swatch.accent')} ${palette.accent}`} />
    </div>
  )
}
