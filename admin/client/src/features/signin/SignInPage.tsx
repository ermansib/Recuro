import { useTranslation } from 'react-i18next'
import { usePersonas } from '../../api/hooks'
import { useSession } from '../../auth/sessionContext'
import { ErrorMessage } from '../../components/ui'

/** Sends people to Keycloak, or lets them pick a persona when the API runs in development sign-in mode. */
export function SignInPage() {
  const { t } = useTranslation()
  const { mode, error, signIn } = useSession()

  return (
    <main className="signin">
      <div className="card">
        <h1>{t('signin.title')}</h1>
        <ErrorMessage error={error} />
        {mode === 'oidc' ? (
          <>
            <p className="muted">{t('signin.oidcHint')}</p>
            <button type="button" className="btn" onClick={() => void signIn()}>
              {t('signin.oidcButton')}
            </button>
          </>
        ) : (
          <PersonaPicker onPick={(id) => void signIn(id)} />
        )}
      </div>
    </main>
  )
}

function PersonaPicker({ onPick }: { onPick: (personaId: string) => void }) {
  const { t } = useTranslation()
  const personas = usePersonas()

  return (
    <>
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
                <button type="button" className="persona" onClick={() => onPick(persona.id)}>
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
    </>
  )
}
