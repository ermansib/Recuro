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
/** Set while a sign-in restarted after a stale callback, so a broken setup can't loop through Keycloak. */
const RETRY_KEY = 'recuro-admin.signin-retry'

/**
 * The sign-in that brought us back to the callback can't be finished, for example because the page was
 * reloaded or reopened from history after the one-time sign-in state was used. Signing in again fixes it.
 */
export class SignInIncompleteError extends Error {
  constructor() {
    super('The sign-in could not be completed.')
    this.name = 'SignInIncompleteError'
  }
}

function isMissingState(reason: unknown): boolean {
  return reason instanceof Error && /no matching state/i.test(reason.message)
}

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

  /** The callback URL being finished, shared so React StrictMode's second effect run doesn't redeem the code twice. */
  let callback: { url: string; done: Promise<void> } | null = null

  const finishCallback = async (url: string): Promise<void> => {
    try {
      await manager.signinRedirectCallback(url)
      safeSession(() => sessionStorage.removeItem(RETRY_KEY), undefined)
    } catch (reason) {
      const user = await manager.getUser()
      if (user && !user.expired) return
      await manager.clearStaleState()
      if (!isMissingState(reason)) throw reason
      // Stale or replayed callback: start a fresh sign-in once (Keycloak's own session usually makes it
      // instant). If that comes back stale too, stop and let the person sign in again by hand.
      if (safeSession(() => sessionStorage.getItem(RETRY_KEY), null) === null) {
        safeSession(() => sessionStorage.setItem(RETRY_KEY, '1'), undefined)
        await manager.signinRedirect()
        return new Promise<void>(() => {})
      }
      safeSession(() => sessionStorage.removeItem(RETRY_KEY), undefined)
      throw new SignInIncompleteError()
    }
  }

  return {
    mode: 'oidc',
    restore: async () => {
      if (location.pathname === CALLBACK_PATH) {
        const url = location.href
        if (callback?.url !== url) callback = { url, done: finishCallback(url) }
        try {
          await callback.done
        } finally {
          // Never leave the one-time code in the address bar: a reload would replay it.
          window.history.replaceState(null, '', '/')
        }
      }
      const user = await manager.getUser()
      return user !== null && !user.expired
    },
    headers: async (): Promise<Record<string, string>> => {
      const user = await manager.getUser()
      return user && !user.expired ? { Authorization: `Bearer ${user.access_token}` } : {}
    },
    signIn: async () => {
      await manager.clearStaleState()
      await manager.signinRedirect()
    },
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
