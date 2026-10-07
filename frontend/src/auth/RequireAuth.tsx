import type { ReactElement } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import { Loading } from '../components/ui'
import type { SignInRedirectState } from '../features/auth/useCompleteSignIn'
import { useAuth } from './sessionContext'

/** Sends signed-out visitors to sign in, then back to the page they asked for. */
export function RequireAuth({ children }: { children: ReactElement }) {
  const { status } = useAuth()
  const location = useLocation()
  if (status === 'restoring') return <Loading />
  if (status === 'signedOut') {
    const state: SignInRedirectState = { from: `${location.pathname}${location.search}` }
    return <Navigate to="/signin" replace state={state} />
  }
  return children
}
