import { useTranslation } from 'react-i18next'
import { usePersonas } from '../../api/hooks'
import { useSession } from '../../auth/sessionContext'

/** Development stand-in for the identity provider's sign-in page. */
export function SignInPage() {
  const { t } = useTranslation()
  const { signIn } = useSession()
  const personas = usePersonas()

  return (
    <main className="signin">
      <div className="card">
        <h1>{t('signin.title')}</h1>
        <p className="notice">{t('signin.devNotice')}</p>
        {personas.isLoading && <p role="status">{t('common.loading')}</p>}
        {personas.error && (
          <p className="error" role="alert">
            {t('signin.unavailable')}
          </p>
        )}
        {personas.data && (
          <>
            <h2>{t('signin.choose')}</h2>
            <ul className="persona-list">
              {personas.data.map((persona) => (
                <li key={persona.id}>
                  <button type="button" className="persona" onClick={() => signIn(persona)}>
                    <strong>{persona.tenantName ?? t('signin.platform')}</strong>
                    <span className="muted">
                      {persona.tenantName ? t('signin.tenantHint', { tenant: persona.tenantName }) : t('signin.platformHint')}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          </>
        )}
      </div>
    </main>
  )
}
