import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation } from 'react-router-dom'
import { api } from '../../api/client'
import type { CompletedSignIn } from '../../domain/types'
import { AuthShell } from './AuthShell'
import { FormError } from './fields'
import { useCompleteSignIn } from './useCompleteSignIn'

// The provider's answer can be read only once, and React runs effects twice in development, so both
// runs share one attempt per callback URL.
let attempt: { url: string; result: Promise<CompletedSignIn> } | null = null

function finishOnce(url: string): Promise<CompletedSignIn> {
  if (attempt?.url !== url) attempt = { url, result: api.completeSignIn() }
  return attempt.result
}

/** Where Keycloak sends people back after they sign in (RCU-PLT-001). */
export function SignInCallbackPage() {
  const { t } = useTranslation()
  const complete = useRef(useCompleteSignIn())
  const [failure, setFailure] = useState<unknown>(null)
  // `code` and `state` in the query string are unique to each sign-in.
  const { search } = useLocation()

  useEffect(() => {
    let active = true
    finishOnce(search).then(
      ({ session, returnTo }) => active && complete.current(session, returnTo),
      (err: unknown) => active && setFailure(err),
    )
    return () => {
      active = false
    }
  }, [search])

  return (
    <AuthShell>
      {failure ? (
        <div>
          <h2>{t('auth.callback.failedTitle')}</h2>
          <FormError error={failure} />
          <Link to="/signin" className="btn btn-primary auth-submit">{t('auth.callback.back')}</Link>
        </div>
      ) : (
        <div className="loading" role="status">{t('auth.callback.working')}</div>
      )}
    </AuthShell>
  )
}
