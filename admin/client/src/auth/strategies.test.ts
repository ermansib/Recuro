import { vi } from 'vitest'
import type { AuthConfig } from '../api/types'
import { CALLBACK_PATH, oidcStrategy, SignInIncompleteError } from './strategies'

const manager = vi.hoisted(() => ({
  signinRedirectCallback: vi.fn<(url?: string) => Promise<unknown>>(),
  signinRedirect: vi.fn<() => Promise<void>>(),
  getUser: vi.fn<() => Promise<{ expired: boolean; access_token: string } | null>>(),
  clearStaleState: vi.fn<() => Promise<void>>(),
}))

vi.mock('oidc-client-ts', () => ({
  UserManager: function UserManager() {
    return manager
  },
  WebStorageStateStore: function WebStorageStateStore() {},
}))

const CONFIG: AuthConfig = { mode: 'oidc', authority: 'http://localhost:8080/realms/recuro', clientId: 'recuro-admin' }
const SIGNED_IN = { expired: false, access_token: 'token' }

function callbackLocation(): Location {
  return {
    origin: 'http://127.0.0.1:5080',
    pathname: CALLBACK_PATH,
    href: `http://127.0.0.1:5080${CALLBACK_PATH}?state=abc&code=xyz`,
  } as Location
}

describe('oidcStrategy callback', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    sessionStorage.clear()
    manager.clearStaleState.mockResolvedValue()
    manager.signinRedirect.mockResolvedValue()
    window.history.replaceState(null, '', CALLBACK_PATH + '?state=abc&code=xyz')
  })

  it('redeems the code once even when restore runs twice (React StrictMode)', async () => {
    let user: typeof SIGNED_IN | null = null
    manager.getUser.mockImplementation(() => Promise.resolve(user))
    manager.signinRedirectCallback.mockImplementation(async () => {
      user = SIGNED_IN
      return user
    })
    const strategy = oidcStrategy(CONFIG, callbackLocation())

    const results = await Promise.all([strategy.restore(), strategy.restore()])

    expect(results).toEqual([true, true])
    expect(manager.signinRedirectCallback).toHaveBeenCalledTimes(1)
    expect(window.location.pathname).toBe('/')
  })

  it('restarts sign-in once when the stored state is gone', async () => {
    manager.getUser.mockResolvedValue(null)
    manager.signinRedirectCallback.mockRejectedValue(new Error('No matching state found in storage'))
    const strategy = oidcStrategy(CONFIG, callbackLocation())

    void strategy.restore()

    await vi.waitFor(() => expect(manager.signinRedirect).toHaveBeenCalledTimes(1))
    expect(manager.clearStaleState).toHaveBeenCalled()
  })

  it('stops with a sign-in-again message when the restarted sign-in comes back stale too', async () => {
    sessionStorage.setItem('recuro-admin.signin-retry', '1')
    manager.getUser.mockResolvedValue(null)
    manager.signinRedirectCallback.mockRejectedValue(new Error('No matching state found in storage'))
    const strategy = oidcStrategy(CONFIG, callbackLocation())

    await expect(strategy.restore()).rejects.toBeInstanceOf(SignInIncompleteError)
    expect(manager.signinRedirect).not.toHaveBeenCalled()
    expect(window.location.pathname).toBe('/')
    expect(sessionStorage.getItem('recuro-admin.signin-retry')).toBeNull()
  })

  it('keeps an existing session when a replayed callback fails', async () => {
    manager.getUser.mockResolvedValue(SIGNED_IN)
    manager.signinRedirectCallback.mockRejectedValue(new Error('No matching state found in storage'))
    const strategy = oidcStrategy(CONFIG, callbackLocation())

    await expect(strategy.restore()).resolves.toBe(true)
    expect(manager.signinRedirect).not.toHaveBeenCalled()
  })

  it('passes other Keycloak errors through', async () => {
    manager.getUser.mockResolvedValue(null)
    manager.signinRedirectCallback.mockRejectedValue(new Error('access_denied'))
    const strategy = oidcStrategy(CONFIG, callbackLocation())

    await expect(strategy.restore()).rejects.toThrow('access_denied')
    expect(manager.signinRedirect).not.toHaveBeenCalled()
  })
})
