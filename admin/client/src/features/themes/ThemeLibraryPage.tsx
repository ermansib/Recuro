import { useTranslation } from 'react-i18next'
import { useSetThemePublished, useThemes } from '../../api/hooks'
import { ErrorMessage, PageHeader, QueryState } from '../../components/ui'
import { Swatches } from './components/Swatches'

/** Platform console: the presets every tenant chooses from. */
export function ThemeLibraryPage() {
  const { t } = useTranslation()
  const themes = useThemes()
  const setPublished = useSetThemePublished()

  return (
    <>
      <PageHeader title={t('themes.title')} subtitle={t('themes.subtitle')} />
      <ErrorMessage error={setPublished.error} />
      <QueryState isLoading={themes.isLoading} error={themes.error}>
        <ul className="theme-grid" style={{ listStyle: 'none', padding: 0 }}>
          {themes.data?.map((preset) => (
            <li key={preset.key} className="theme-card">
              <span className={`badge tag ${preset.isPublished ? 'ok' : 'neutral'}`}>
                {preset.isPublished ? t('themes.published') : t('themes.draft')}
              </span>
              <div className="theme-name">{preset.name}</div>
              <div className="library-modes">
                <div>
                  <Swatches palette={preset.light} />
                  <span className="hex">{t('themes.light')}</span>
                </div>
                <div>
                  <Swatches palette={preset.dark} />
                  <span className="hex">{t('themes.dark')}</span>
                </div>
              </div>
              <button
                type="button"
                className="btn secondary small"
                disabled={setPublished.isPending}
                aria-label={`${preset.isPublished ? t('themes.unpublish') : t('themes.publish')}: ${preset.name}`}
                onClick={() => setPublished.mutate({ key: preset.key, isPublished: !preset.isPublished })}
              >
                {preset.isPublished ? t('themes.unpublish') : t('themes.publish')}
              </button>
            </li>
          ))}
        </ul>
      </QueryState>
    </>
  )
}
