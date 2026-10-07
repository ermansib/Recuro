import { UserManager, WebStorageStateStore } from 'oidc-client-ts'
import { PERSONA_HEADER } from '../api/httpClient'
import type { AuthConfig } from '../api/types'

/** One way of signing in. The API's /auth/config decides which one the client uses. */
export interface AuthStrategy {
  mode: AuthConfig['mode']
  /** Restores an existing session, finishing a Keycloak redirect if one is in progress. True when signed in. */
  restore(): Promise<boolean>
  /** Headers that identify the caller on API requests. */
  headers(): Promise<Record<string, string>>
  /** Development: signs in as the persona. Keycloak: redirects to the sign-in page. */
  signIn(personaId?: string): Promise<void>
  signOut(): Promise<void>
}

export const CALLBACK_PATH = '/auth/callback'
const PERSONA_KEY = 'recuro-admin.persona'

function safeSession<T>(action: () => T, fallback: T): T {
  try {
    return action()
  } catch {
    return fallback
  }
}

/** Local development without Keycloak: the persona travels in a header the API trusts only in Development. */
export function developmentStrategy(): AuthStrategy {
  const persona = () => safeSession(() => sessionStorage.getItem(PERSONA_KEY), null)
  return {
    mode: 'development',
    restore: () => Promise.resolve(persona() !== null),
    headers: () => {
      const id = persona()
      const headers: Record<string, string> = id ? { [PERSONA_HEADER]: id } : {}
      return Promise.resolve(headers)
    },
    signIn: (personaId) => {
      if (personaId) safeSession(() => sessionStorage.setItem(PERSONA_KEY, personaId), undefined)
      return Promise.resolve()
    },
    signOut: () => {
      safeSession(() => sessionStorage.removeItem(PERSONA_KEY), undefined)
      return Promise.resolve()
    },
  }
}

/** Keycloak (or any OpenID Connect provider): authorization code flow with PKCE, tokens kept per tab. */
export function oidcStrategy(config: AuthConfig, location: Location = window.location): AuthStrategy {
  const origin = location.origin
  const manager = new UserManager({
    authority: config.authority ?? '',
    client_id: config.clientId ?? '',
    redirect_uri: `${origin}${CALLBACK_PATH}`,
    post_logout_redirect_uri: `${origin}/`,
    response_type: 'code',
    scope: 'openid profile email',
    automaticSilentRenew: true,
    userStore: new WebStorageStateStore({ store: window.sessionStorage }),
  })

  return {
    mode: 'oidc',
    restore: async () => {
      if (location.pathname === CALLBACK_PATH) {
        await manager.signinRedirectCallback()
        window.history.replaceState(null, '', '/')
      }
      const user = await manager.getUser()
      return user !== null && !user.expired
    },
    headers: async (): Promise<Record<string, string>> => {
      const user = await manager.getUser()
      return user && !user.expired ? { Authorization: `Bearer ${user.access_token}` } : {}
    },
    signIn: () => manager.signinRedirect(),
    signOut: async () => {
      const user = await manager.getUser()
      await manager.removeUser()
      await manager.signoutRedirect({ id_token_hint: user?.id_token })
    },
  }
}

export function strategyFor(config: AuthConfig): AuthStrategy {
  return config.mode === 'oidc' ? oidcStrategy(config) : developmentStrategy()
}
