import { useCallback } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { homePath } from '../../auth/permissions'
import { useAuth } from '../../auth/sessionContext'
import type { AuthSession } from '../../domain/types'

/** Where the person was going before being asked to sign in, set by RequireAuth. */
export interface SignInRedirectState {
  from?: string
}

/** Stores the session and sends the person to the page they asked for, or their role's landing page. */
export function useCompleteSignIn() {
  const { establish } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const from = (location.state as SignInRedirectState | null)?.from

  return useCallback(
    (session: AuthSession) => {
      establish(session)
      // Routes re-check the role, so an unsuitable `from` still ends on a page this role can see.
      navigate(from ?? homePath(session.user.role), { replace: true })
    },
    [establish, navigate, from],
  )
}
