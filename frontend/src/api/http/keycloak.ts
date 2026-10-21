// Keycloak (or any OpenID Connect provider) sign-in for the portal: authorization code flow with PKCE
// through oidc-client-ts, the same library the admin portal uses. Keycloak checks the password, lockout
// and second factor; the portal only reads who signed in from the tokens it gets back.
//
// Tokens live in localStorage so every tab of the portal shares one sign-in, like the rest of the session.
// The UI reads claims without verifying the signature: that is a convenience only, the .NET services
// validate every token they receive.
import { UserManager, WebStorageStateStore, type User as OidcUser } from 'oidc-client-ts'
import { identityProvider as settings } from '../../config/identity'
import { captureSignInLook, encodeSignInBrand, SIGN_IN_BRAND_PARAM } from './signInBrand'

/** Who the identity provider signed in, read from its tokens. */
export interface IdentityAccount {
  /** Keycloak subject (user id). */
  subject: string
  name: string
  email: string
  /** `tenant_id` claim: the workspace the account belongs to. */
  tenantId: string | null
  /** Realm roles (`roles` claim). */
  roles: string[]
  /** Role keys the workspace assigned, including roles the workspace created itself (`workspace_roles` claim). */
  workspaceRoles: string[]
  /** Bearer token for Recuro APIs that act as the signed-in person. */
  accessToken: string
  /** Workspace slug the sign-in was started for. */
  workspace: string | null
  /** Page to open after sign-in. */
  returnTo: string | null
}

export interface IdentityProvider {
  /**
   * Sends the browser to the provider's sign-in page. Resolves only if navigation is blocked. With `brand`,
   * the page shows the workspace's name and the portal's current colours (see signInBrand.ts).
   */
  startSignIn(request: { workspace: string; email?: string; returnTo?: string; brand?: { name: string; tagline: string } }): Promise<void>
  /** Reads the provider's answer on the callback page. */
  finishSignIn(): Promise<IdentityAccount>
  /** The signed-in account, renewing expired tokens with the refresh token; `null` when there is none. */
  currentAccount(): Promise<IdentityAccount | null>
  /** Forgets the tokens in this browser without contacting the provider. */
  forget(): Promise<void>
  /** Ends the provider's session too, then returns to `returnPath` on the portal. */
  signOut(returnPath: string): Promise<void>
}

/** oidc-client-ts doesn't keep the sign-in state with the stored user, so the workspace is kept here. */
const WORKSPACE_KEY = 'recuro.idp.workspace'

function remember(workspace: string | null): void {
  try {
    if (workspace) localStorage.setItem(WORKSPACE_KEY, workspace)
    else localStorage.removeItem(WORKSPACE_KEY)
  } catch {
    // Storage unavailable (private mode): the sign-in then lasts for this page only.
  }
}

function remembered(): string | null {
  try {
    return localStorage.getItem(WORKSPACE_KEY)
  } catch {
    return null
  }
}

interface SignInState {
  workspace: string
  returnTo: string | null
}

/** Decodes a JWT's payload. Returns an empty object for anything that isn't a readable JWT. */
export function readJwtClaims(token: string): Record<string, unknown> {
  const payload = token.split('.')[1]
  if (!payload) return {}
  try {
    const base64 = payload.replaceAll('-', '+').replaceAll('_', '/').padEnd(Math.ceil(payload.length / 4) * 4, '=')
    const json = new TextDecoder().decode(Uint8Array.from(atob(base64), (c) => c.charCodeAt(0)))
    const claims: unknown = JSON.parse(json)
    return typeof claims === 'object' && claims !== null ? (claims as Record<string, unknown>) : {}
  } catch {
    return {}
  }
}

const text = (value: unknown): string | null => (typeof value === 'string' && value.trim() ? value : null)

const texts = (value: unknown): string[] => (Array.isArray(value) ? value.filter((v): v is string => typeof v === 'string') : [])

function isSignInState(value: unknown): value is SignInState {
  return typeof value === 'object' && value !== null && 'workspace' in value && typeof value.workspace === 'string'
}

/** Builds the account from the ID token profile (name, email) and the access token (tenant, roles). */
export function accountOf(user: Pick<OidcUser, 'access_token' | 'profile' | 'state'>, workspace: string | null = null): IdentityAccount {
  const access = readJwtClaims(user.access_token)
  const profile = user.profile as Record<string, unknown>
  const email = text(profile.email) ?? text(access.email) ?? ''
  const state = isSignInState(user.state) ? user.state : null
  return {
    subject: text(profile.sub) ?? text(access.sub) ?? '',
    name: text(profile.name) ?? text(access.name) ?? text(profile.preferred_username) ?? email,
    email,
    tenantId: text(access.tenant_id),
    roles: texts(access.roles),
    workspaceRoles: texts(access.workspace_roles),
    accessToken: user.access_token,
    workspace: state?.workspace ?? workspace,
    returnTo: state?.returnTo ?? null,
  }
}

export function createKeycloakIdentityProvider(origin: string = window.location.origin): IdentityProvider {
  let manager: UserManager | null = null
  // Created on first use, so pages that never sign in with Keycloak never load its metadata.
  const users = () =>
    (manager ??= new UserManager({
      authority: settings.authority,
      client_id: settings.clientId,
      redirect_uri: `${origin}${settings.callbackPath}`,
      post_logout_redirect_uri: `${origin}/signin`,
      response_type: 'code',
      scope: 'openid profile email',
      automaticSilentRenew: true,
      userStore: new WebStorageStateStore({ store: window.localStorage }),
    }))

  return {
    startSignIn: ({ workspace, email, returnTo, brand }) =>
      users().signinRedirect({
        login_hint: email || undefined,
        state: { workspace, returnTo: returnTo ?? null } satisfies SignInState,
        extraQueryParams: brand
          ? { [SIGN_IN_BRAND_PARAM]: encodeSignInBrand({ v: 1, slug: workspace, ...brand, ...captureSignInLook() }) }
          : undefined,
      }),

    finishSignIn: async () => {
      const account = accountOf(await users().signinRedirectCallback())
      remember(account.workspace)
      return account
    },

    currentAccount: async () => {
      const mgr = users()
      let user = await mgr.getUser()
      if (!user) return null
      if (user.expired) {
        try {
          user = await mgr.signinSilent()
        } catch {
          await mgr.removeUser()
          return null
        }
        if (!user) return null
      }
      return accountOf(user, remembered())
    },

    forget: async () => {
      remember(null)
      await users().removeUser()
    },

    signOut: async (returnPath) => {
      const mgr = users()
      const user = await mgr.getUser()
      remember(null)
      await mgr.removeUser()
      await mgr.signoutRedirect({ id_token_hint: user?.id_token, post_logout_redirect_uri: `${origin}${returnPath}` })
    },
  }
}
