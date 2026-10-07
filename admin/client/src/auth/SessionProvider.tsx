import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useMemo, useState, type ReactNode } from 'react'
import type { Persona } from '../api/types'
import { levelOf, readPersona, writePersona } from './personaStorage'
import { SessionContext, type Session } from './sessionContext'

interface SessionProviderProps {
  children: ReactNode
  initialPersona?: Persona | null
}

export function SessionProvider({ children, initialPersona }: SessionProviderProps) {
  const queryClient = useQueryClient()
  const [persona, setPersona] = useState<Persona | null>(() => initialPersona ?? readPersona())

  const signIn = useCallback(
    (next: Persona) => {
      writePersona(next)
      queryClient.clear()
      setPersona(next)
    },
    [queryClient],
  )

  const signOut = useCallback(() => {
    writePersona(null)
    queryClient.clear()
    setPersona(null)
  }, [queryClient])

  const value = useMemo<Session>(
    () => ({ persona, level: persona ? levelOf(persona) : null, signIn, signOut }),
    [persona, signIn, signOut],
  )

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>
}
