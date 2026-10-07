import { createContext, useContext } from 'react'
import type { Actor } from '../api/contract'
import type { Role, User } from '../domain/types'

export interface SessionValue {
  user: User
  users: User[]
  actor: Actor
  switchRole: (role: Role) => void
}

export const SessionContext = createContext<SessionValue | null>(null)

export function useSession(): SessionValue {
  const ctx = useContext(SessionContext)
  if (!ctx) throw new Error('useSession must be used inside SessionProvider')
  return ctx
}
