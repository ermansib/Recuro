// Demo sign-in: a persona switcher stands in for SSO until the identity provider is wired
// (RCU-PLT-001). Everything downstream reads the current user from here, so swapping in real
// auth only changes this provider.
import { useCallback, useMemo, useState, type ReactNode } from 'react'
import type { Role, User } from '../domain/types'
import { SessionContext } from './sessionContext'


const STORAGE_KEY = 'recuro.persona'

function readStoredRole(): Role | null {
  try {
    return (localStorage.getItem(STORAGE_KEY) as Role | null) ?? null
  } catch {
    return null
  }
}

export function SessionProvider({ users, initialRole, children }: { users: User[]; initialRole?: Role; children: ReactNode }) {
  const [role, setRole] = useState<Role>(() => initialRole ?? readStoredRole() ?? 'hrta')
  const user = users.find((u) => u.role === role) ?? users[0]
  if (!user) throw new Error('No users configured')

  const switchRole = useCallback((next: Role) => {
    setRole(next)
    try {
      localStorage.setItem(STORAGE_KEY, next)
    } catch {
      // Storage can be unavailable (private mode); the persona just won't persist.
    }
  }, [])

  const value = useMemo(
    () => ({ user, users, actor: { name: user.name, role: user.role }, switchRole }),
    [user, users, switchRole],
  )
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>
}
