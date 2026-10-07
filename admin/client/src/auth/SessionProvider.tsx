import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useApi } from '../api/client'
import type { CurrentAdmin } from '../api/types'
import { setCredentialProvider } from './credentials'
import { SessionContext, type Session, type SessionStatus } from './sessionContext'
import type { AuthStrategy } from './strategies'

interface SessionProviderProps {
  children: ReactNode
  strategy: AuthStrategy
  /** Tests start signed in without going through a strategy. */
  initialAdmin?: CurrentAdmin
}

export function SessionProvider({ children, strategy, initialAdmin }: SessionProviderProps) {
  const api = useApi()
  const queryClient = useQueryClient()
  const [admin, setAdmin] = useState<CurrentAdmin | null>(initialAdmin ?? null)
  const [status, setStatus] = useState<SessionStatus>(initialAdmin ? 'signedIn' : 'loading')
  const [error, setError] = useState<Error | null>(null)

  const loadAdmin = useCallback(async () => {
    try {
      setAdmin(await api.getMe())
      setError(null)
      setStatus('signedIn')
    } catch (reason) {
      setAdmin(null)
      setError(reason instanceof Error ? reason : new Error(String(reason)))
      setStatus('signedOut')
    }
  }, [api])

  useEffect(() => {
    setCredentialProvider(() => strategy.headers())
    if (initialAdmin) return
    let active = true
    strategy
      .restore()
      .then((signedIn) => {
        if (!active) return
        if (signedIn) return loadAdmin()
        setStatus('signedOut')
      })
      .catch((reason: unknown) => {
        if (!active) return
        setError(reason instanceof Error ? reason : new Error(String(reason)))
        setStatus('signedOut')
      })
    return () => {
      active = false
    }
  }, [strategy, initialAdmin, loadAdmin])

  const signIn = useCallback(
    async (personaId?: string) => {
      setError(null)
      await strategy.signIn(personaId)
      // Keycloak has redirected away by now; the development sign-in continues here.
      if (strategy.mode === 'development') {
        queryClient.clear()
        await loadAdmin()
      }
    },
    [strategy, queryClient, loadAdmin],
  )

  const signOut = useCallback(async () => {
    queryClient.clear()
    setAdmin(null)
    setStatus('signedOut')
    await strategy.signOut()
  }, [strategy, queryClient])

  const value = useMemo<Session>(
    () => ({ status, mode: strategy.mode, admin, level: admin?.level ?? null, error, signIn, signOut }),
    [status, strategy.mode, admin, error, signIn, signOut],
  )

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>
}
