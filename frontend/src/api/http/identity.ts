// Sign-in for workspaces saved on the server (RCU-PLT-001). The admin API knows the workspace
// (GET /api/v1/workspaces/{slug}); Keycloak knows its people and checks their passwords. A workspace the
// server doesn't have (the in-browser demo, or any workspace while the server is down in a demo) keeps
// using the in-browser identity, so the demo personas still work in development.
//
// Every session made here carries the workspace's tenant id, and an account whose `tenant_id` claim
// names another workspace is refused, so one tenant's people can never open another tenant's portal.
import { initialsOf, isValidEmail, normaliseEmail, slugify, unmetPasswordRules } from '../../domain/auth'
import type { AuthSession, Role, TenantConfig, User, Workspace, WorkspaceBranding } from '../../domain/types'
import { personaFor } from '../../auth/permissions'
import { ApiError, type AuthApi, type WorkspaceApi } from '../contract'
import type { IdentityAccount, IdentityProvider } from './keycloak'

/** Session handles for identity-provider sign-ins start with this; the tokens stay in the provider's store. */
export const IDP_TOKEN_PREFIX = 'idp:'

const NOT_FINISHED = 'We couldn’t finish signing you in. Please start again from the sign-in page.'
const WRONG_WORKSPACE = 'This account belongs to a different workspace. Sign in to your own workspace instead.'
const NO_ROLE = 'Your account has no Recuro role in this workspace yet. Ask your administrator to give you one.'
const SESSION_ENDED = 'Your session has ended. Please sign in again.'
const SIGN_IN_DOWN = 'We couldn’t reach the sign-in service. Please try again in a few minutes.'

export interface IdentityProviderAuthDeps {
  /** In-browser identity for workspaces the server doesn't have (demo, tests, offline). */
  local: AuthApi
  workspaces: WorkspaceApi
  provider: IdentityProvider
  /** Defaults for tenant settings the server doesn't send yet (careers copy, theme fallback). */
  defaults: TenantConfig
}

type Resolved = { kind: 'server'; workspace: Workspace } | { kind: 'local' }

const isIdpToken = (token: string) => token.startsWith(IDP_TOKEN_PREFIX)

export function brandingOfWorkspace(ws: Workspace): WorkspaceBranding {
  return {
    slug: ws.slug,
    name: ws.name,
    orgType: ws.orgType,
    careersTagline: ws.careersTagline,
    theme: undefined,
    ssoProviders: ws.ssoProviders,
    sessionIdleMinutes: ws.sessionIdleMinutes,
    signInFlow: 'redirect',
  }
}

export function createIdentityProviderAuthApi({ local, workspaces, provider, defaults }: IdentityProviderAuthDeps): AuthApi {
  /** Which identity owns the workspace. While the server is down, only workspaces this browser knows open. */
  async function resolve(slug: string): Promise<Resolved> {
    // People type the workspace's name ("MB Consulting") as often as its address (mb-consulting). The server
    // matches either; the address is tried too in case the name differs from what made the address.
    const wanted = slug.trim().toLowerCase()
    const address = slugify(wanted)
    try {
      const workspace = (await workspaces.getWorkspace(wanted)) ?? (address && address !== wanted ? await workspaces.getWorkspace(address) : null)
      return workspace ? { kind: 'server', workspace } : { kind: 'local' }
    } catch (serverDown) {
      const knownHere = await local.getWorkspaceBranding(wanted).then(
        () => true,
        () => false,
      )
      if (knownHere) return { kind: 'local' }
      throw serverDown
    }
  }

  const tenantOf = (ws: Workspace): TenantConfig => ({
    ...defaults,
    ...ws,
    legalName: ws.name,
    careersIntro: '',
    emailDomain: ws.emailDomain ?? '',
    // The admin portal serves the tenant's theme separately (GET /api/v1/runtime/{slug}).
    theme: undefined,
  })

  /**
   * The person's roles and permissions as the workspace assigned them (admin API). While the admin API can't answer,
   * the role keys in the token stand in, without permissions, so persona roles still sign in.
   */
  async function accessOf(account: IdentityAccount): Promise<{ roleKeys: string[]; permissions: string[] }> {
    try {
      const access = await workspaces.getMyAccess(account.accessToken)
      return { roleKeys: access.roles.map((r) => r.key), permissions: access.permissions }
    } catch (e) {
      if (e instanceof ApiError && e.status !== 503) throw e
      return { roleKeys: [...account.roles, ...account.workspaceRoles], permissions: [] }
    }
  }

  /** Turns the provider's account into a portal session for `ws`, or refuses it. */
  async function sessionFor(account: IdentityAccount, ws: Workspace, issuedAt: string): Promise<AuthSession> {
    if (!account.subject) throw new ApiError(401, NOT_FINISHED)
    if (account.tenantId !== ws.id) throw new ApiError(403, WRONG_WORKSPACE)
    const { roleKeys, permissions } = await accessOf(account)
    const role: Role | null = personaFor(roleKeys, permissions)
    if (!role) throw new ApiError(403, NO_ROLE)
    const user: User = {
      id: account.subject,
      tenantId: ws.id,
      name: account.name,
      initials: initialsOf(account.name),
      role,
      title: '',
      email: normaliseEmail(account.email),
      summary: '',
      permissions,
    }
    return { token: `${IDP_TOKEN_PREFIX}${account.subject}`, user, tenant: tenantOf(ws), issuedAt, viaIdentityProvider: true }
  }

  /** The signed-in identity-provider session behind `token`, or 401. */
  async function current(token: string): Promise<AuthSession> {
    const account = await provider.currentAccount()
    if (!account || `${IDP_TOKEN_PREFIX}${account.subject}` !== token || !account.workspace) throw new ApiError(401, SESSION_ENDED)
    const ws = await workspaces.getWorkspace(account.workspace)
    if (!ws) {
      await provider.forget()
      throw new ApiError(401, SESSION_ENDED)
    }
    return await sessionFor(account, ws, new Date().toISOString())
  }

  return {
    ...local,

    getWorkspaceBranding: async (slug) => {
      const resolved = await resolve(slug)
      return resolved.kind === 'server' ? brandingOfWorkspace(resolved.workspace) : local.getWorkspaceBranding(slug)
    },

    signIn: async (input) => {
      const resolved = await resolve(input.workspace)
      if (resolved.kind === 'local') return local.signIn(input)
      try {
        const { slug, name, careersTagline } = resolved.workspace
        await provider.startSignIn({ workspace: slug, email: input.email.trim(), returnTo: input.returnTo, brand: { name, tagline: careersTagline } })
      } catch {
        throw new ApiError(503, SIGN_IN_DOWN)
      }
      return { status: 'redirecting' }
    },

    signInWithSso: async (slug, sso) => {
      const resolved = await resolve(slug)
      if (resolved.kind === 'local') return local.signInWithSso(slug, sso)
      throw new ApiError(501, 'Single sign-on isn’t connected for this workspace yet. Use your email and password.')
    },

    completeSignIn: async () => {
      let account: IdentityAccount
      try {
        account = await provider.finishSignIn()
      } catch {
        throw new ApiError(401, NOT_FINISHED)
      }
      try {
        const ws = account.workspace ? await workspaces.getWorkspace(account.workspace) : null
        if (!ws) throw new ApiError(404, 'We couldn’t find that workspace. Check the name with your administrator.')
        return { session: await sessionFor(account, ws, new Date().toISOString()), returnTo: account.returnTo }
      } catch (refused) {
        // Nothing of a refused sign-in stays in this browser.
        await provider.forget()
        throw refused
      }
    },

    getSession: (token) => (isIdpToken(token) ? current(token) : local.getSession(token)),

    signOut: async (token) => {
      if (!isIdpToken(token)) return local.signOut(token)
      const account = await provider.currentAccount().catch(() => null)
      const back = account?.workspace ? `/signin?workspace=${encodeURIComponent(account.workspace)}` : '/signin'
      await provider.signOut(back)
    },

    registerOrganisation: async (input) => {
      // The server checks again; checking here saves a round trip for a form that can't succeed.
      if (!input.orgName.trim() || !input.adminName.trim()) throw new ApiError(400, 'Organisation and your name are required')
      if (!isValidEmail(input.email)) throw new ApiError(400, 'Enter a valid email address')
      if (unmetPasswordRules(input.password).length > 0) throw new ApiError(400, 'Password does not meet the policy')
      if (!input.acceptTerms) throw new ApiError(400, 'Accept the terms to create a workspace')
      const email = normaliseEmail(input.email)
      const saved = await workspaces.registerWorkspace({ ...input, email })
      // The owner signs in once with the password they just chose; Keycloak then keeps them signed in.
      try {
        const { slug, name, careersTagline } = saved.workspace
        await provider.startSignIn({ workspace: slug, email, brand: { name, tagline: careersTagline } })
      } catch {
        throw new ApiError(
          503,
          `Your workspace ${saved.workspace.name} is ready, but we couldn’t open the sign-in page. Sign in at /signin?workspace=${saved.workspace.slug} in a few minutes.`,
        )
      }
      return { status: 'redirecting' }
    },

    // Team and invitations for server workspaces move to the Identity service next; until then the
    // signed-in person is the team they can see.
    listTeam: async (token) => (isIdpToken(token) ? [(await current(token)).user] : local.listTeam(token)),
    listInvitations: async (token) => {
      if (!isIdpToken(token)) return local.listInvitations(token)
      await current(token)
      return []
    },
    inviteStaff: async (token, input) => {
      if (!isIdpToken(token)) return local.inviteStaff(token, input)
      throw new ApiError(501, 'Inviting people to this workspace isn’t available yet.')
    },
  }
}
