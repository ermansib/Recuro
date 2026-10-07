import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { useScreens } from '../../api/hooks'
import { PageHeader, QueryState } from '../../components/ui'

/** Tenant admin: every screen of the portal and whether it is on. */
export function ScreensPage() {
  const { t } = useTranslation()
  const screens = useScreens()

  return (
    <>
      <PageHeader title={t('screens.title')} subtitle={t('screens.subtitle')} />
      <section className="card">
        <QueryState isLoading={screens.isLoading} error={screens.error}>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th scope="col">{t('screens.columns.code')}</th>
                  <th scope="col">{t('screens.columns.screen')}</th>
                  <th scope="col">{t('screens.columns.module')}</th>
                  <th scope="col">{t('screens.columns.fields')}</th>
                  <th scope="col">{t('screens.columns.status')}</th>
                  <th scope="col">
                    <span className="sr-only">{t('screens.edit')}</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {screens.data?.map((screen) => (
                  <tr key={screen.key}>
                    <td className="hex">{screen.code}</td>
                    <th scope="row">
                      {screen.title}
                      <div className="muted" style={{ fontWeight: 400, fontSize: 12 }}>
                        {screen.subtitle}
                      </div>
                    </th>
                    <td>{screen.module}</td>
                    <td>{screen.fields.length}</td>
                    <td>
                      <span className={`badge ${screen.isEnabled ? 'ok' : 'off'}`}>
                        {screen.isEnabled ? t('screens.on') : t('screens.off')}
                      </span>
                    </td>
                    <td>
                      <Link className="btn secondary small" to={`/tenant/screens/${screen.key}`} aria-label={`${t('screens.edit')}: ${screen.title}`}>
                        {t('screens.edit')}
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </QueryState>
      </section>
    </>
  )
}
