import { createContext, useContext } from 'react'
import type { Actor } from '../api/contract'
import type { AuthSession, Role, TenantConfig, User } from '../domain/types'

export type SignOutReason = 'user' | 'idle' | 'expired'

export interface AuthValue {
  status: 'restoring' | 'signedOut' | 'signedIn'
  session: AuthSession | null
  /** Why the last session ended, so the sign-in page can explain it. */
  signedOutReason: SignOutReason | null
  /** Workspace slug used last on this device, so the sign-in page can show its branding. */
  lastWorkspace: string | null
  establish: (session: AuthSession) => void
  signOut: (reason?: SignOutReason) => Promise<void>
}

/** The signed-in view of the session. Only available inside protected routes. */
export interface SessionValue {
  user: User
  tenant: TenantConfig
  token: string
  actor: Actor
  /** Demo persona switcher (dev/demo builds only). */
  switchRole: (role: Role) => Promise<void>
  signOut: () => Promise<void>
}

export const AuthContext = createContext<AuthValue | null>(null)
export const SessionContext = createContext<SessionValue | null>(null)

export function useAuth(): AuthValue {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}

export function useSession(): SessionValue {
  const ctx = useContext(SessionContext)
  if (!ctx) throw new Error('useSession must be used inside a signed-in route')
  return ctx
}
