import { createContext, useContext } from 'react'
import type { Persona } from '../api/types'
import type { AdminLevel } from './personaStorage'

export interface Session {
  persona: Persona | null
  level: AdminLevel | null
  signIn: (persona: Persona) => void
  signOut: () => void
}

export const SessionContext = createContext<Session | null>(null)

export function useSession(): Session {
  const session = useContext(SessionContext)
  if (!session) throw new Error('useSession must be used inside SessionProvider')
  return session
}
