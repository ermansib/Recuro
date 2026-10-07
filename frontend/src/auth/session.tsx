// Session lifecycle (RCU-PLT-001): sign-in, restore after reload, idle timeout (NFR-01) and
// sign-out. It talks to the identity endpoints of the ApiClient only, so the .NET identity
// service (or an OIDC redirect) replaces the mock without touching screens.
import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api } from '../api/client'
import type { AuthSession, Role } from '../domain/types'
import { AuthContext, SessionContext, type AuthValue, type SessionValue, type SignOutReason } from './sessionContext'
import { useTenantTheme } from '../theme/themeContext'
import { useIdleTimeout } from './useIdleTimeout'

export const SESSION_STORAGE_KEY = 'recuro.session'
export const WORKSPACE_STORAGE_KEY = 'recuro.workspace'

function read(key: string): string | null {
  try {
    return localStorage.getItem(key)
  } catch {
    return null
  }
}

function write(key: string, value: string | null): void {
  try {
    if (value === null) localStorage.removeItem(key)
    else localStorage.setItem(key, value)
  } catch {
    // Storage can be unavailable (private mode); the session then lasts for this page only.
  }
}

export function AuthProvider({ initialSession, children }: { initialSession?: AuthSession; children: ReactNode }) {
  const queryClient = useQueryClient()
  const [session, setSession] = useState<AuthSession | null>(initialSession ?? null)
  const [restoring, setRestoring] = useState(() => !initialSession && read(SESSION_STORAGE_KEY) !== null)
  const [signedOutReason, setSignedOutReason] = useState<SignOutReason | null>(null)
  const [lastWorkspace, setLastWorkspace] = useState(() => initialSession?.tenant.slug ?? read(WORKSPACE_STORAGE_KEY))

  useEffect(() => {
    if (!restoring) return
    let cancelled = false
    const token = read(SESSION_STORAGE_KEY) ?? ''
    api.getSession(token).then(
      (restored) => {
        if (cancelled) return
        setSession(restored)
        setLastWorkspace(restored.tenant.slug)
      },
      () => {
        if (cancelled) return
        write(SESSION_STORAGE_KEY, null)
        setSignedOutReason('expired')
      },
    ).finally(() => !cancelled && setRestoring(false))
    return () => {
      cancelled = true
    }
  }, [restoring])

  // Signed in: the tenant's theme. Signed out: the sign-in page (AuthShell) picks the workspace's.
  useTenantTheme(session ? { slug: session.tenant.slug, fallback: session.tenant.theme } : undefined)

  const establish = useCallback((next: AuthSession) => {
    write(SESSION_STORAGE_KEY, next.token)
    write(WORKSPACE_STORAGE_KEY, next.tenant.slug)
    setLastWorkspace(next.tenant.slug)
    setSignedOutReason(null)
    setSession(next)
  }, [])

  const endLocally = useCallback(
    (reason: SignOutReason) => {
      write(SESSION_STORAGE_KEY, null)
      setSession(null)
      setSignedOutReason(reason)
      // Nothing from the previous user may stay in the cache for the next one.
      queryClient.clear()
    },
    [queryClient],
  )

  const signOut = useCallback(
    async (reason: SignOutReason = 'user') => {
      const token = session?.token
      endLocally(reason)
      if (token) await api.signOut(token).catch(() => undefined)
    },
    [session?.token, endLocally],
  )

  // Signing out in one tab signs out the others.
  useEffect(() => {
    const onStorage = (e: StorageEvent) => {
      if (e.key === SESSION_STORAGE_KEY && e.newValue === null && session) endLocally('user')
    }
    window.addEventListener('storage', onStorage)
    return () => window.removeEventListener('storage', onStorage)
  }, [session, endLocally])

  const onIdle = useCallback(() => void signOut('idle'), [signOut])
  useIdleTimeout(session ? session.tenant.sessionIdleMinutes : null, onIdle)

  const switchRole = useCallback(
    async (role: Role) => {
      if (!session) return
      const next = await api.demoSignIn(session.tenant.slug, role)
      await api.signOut(session.token).catch(() => undefined)
      establish(next)
    },
    [session, establish],
  )

  const auth = useMemo<AuthValue>(
    () => ({
      status: restoring ? 'restoring' : session ? 'signedIn' : 'signedOut',
      session,
      signedOutReason,
      lastWorkspace,
      establish,
      signOut,
    }),
    [restoring, session, signedOutReason, lastWorkspace, establish, signOut],
  )

  const signedIn = useMemo<SessionValue | null>(
    () =>
      session && {
        user: session.user,
        tenant: session.tenant,
        token: session.token,
        actor: { name: session.user.name, role: session.user.role },
        switchRole,
        signOut: () => signOut('user'),
      },
    [session, switchRole, signOut],
  )

  return (
    <AuthContext.Provider value={auth}>
      <SessionContext.Provider value={signedIn}>{children}</SessionContext.Provider>
    </AuthContext.Provider>
  )
}
