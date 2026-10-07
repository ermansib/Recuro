import { createContext, useContext } from 'react'
import type { AdminLevel, AuthConfig, CurrentAdmin } from '../api/types'

export type SessionStatus = 'loading' | 'signedOut' | 'signedIn'

export interface Session {
  status: SessionStatus
  mode: AuthConfig['mode']
  admin: CurrentAdmin | null
  level: AdminLevel | null
  /** Why the last sign-in did not work, for example an account without an admin role. */
  error: Error | null
  signIn: (personaId?: string) => Promise<void>
  signOut: () => Promise<void>
}

export const SessionContext = createContext<Session | null>(null)

export function useSession(): Session {
  const session = useContext(SessionContext)
  if (!session) throw new Error('useSession must be used inside SessionProvider')
  return session
}
