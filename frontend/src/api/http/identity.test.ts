import { describe, expect, it, vi } from 'vitest'
import { tenant as defaultTenant } from '../../config/tenant'
import type { Workspace } from '../../domain/types'
import { ApiError, type WorkspaceApi } from '../contract'
import { createMockClient } from '../mock/mockClient'
import { DEMO_PASSWORD } from '../mock/seed'
import { IDP_TOKEN_PREFIX, createIdentityProviderAuthApi } from './identity'
import type { IdentityAccount, IdentityProvider } from './keycloak'

const MB: Workspace = {
  id: '7c0e2b8a-4d1f-4c3e-9a55-2f1e0d9c0001',
  slug: 'mb-consulting',
  name: 'MB Consulting',
  orgType: 'agency',
  careersTagline: 'Find your next role through us',
  emailDomain: 'mbconsulting.example',
  locale: 'en-IN',
  currency: 'INR',
  ssoProviders: ['google', 'microsoft'],
  mfaRoles: ['hrhead', 'mdceo'],
  sessionIdleMinutes: 30,
}

const OWNER: IdentityAccount = {
  subject: 'kc-sub-mb',
  name: 'Mansi Bansal',
  email: 'Owner@MBConsulting.example',
  tenantId: MB.id,
  roles: ['default-roles-recuro', 'tenant-admin', 'hrhead'],
  workspace: MB.slug,
  returnTo: '/approvals',
}

const SIGN_UP = {
  orgName: 'MB Consulting',
  orgType: 'agency' as const,
  adminName: 'Mansi Bansal',
  adminRole: 'hrhead' as const,
  email: 'Owner@MBConsulting.example',
  password: 'Str0ng!pass',
  acceptTerms: true,
}

/** The admin API: knows MB Consulting only, or is down. */
function server({ down = false } = {}): WorkspaceApi {
  const unreachable = () => Promise.reject(new ApiError(503, 'We couldn’t reach the Recuro server.'))
  return {
    getWorkspace: vi.fn((slug: string) => (down ? unreachable() : Promise.resolve(slug === MB.slug ? MB : null))),
    registerWorkspace: vi.fn(() => (down ? unreachable() : Promise.resolve({ workspace: MB, owner: { id: OWNER.subject, name: OWNER.name, email: 'owner@mbconsulting.example', role: 'hrhead' as const } }))),
  }
}

/** Keycloak, as far as the portal can see it. */
function keycloak(account: IdentityAccount | null = OWNER) {
  let signedIn: IdentityAccount | null = null
  const provider = {
    startSignIn: vi.fn(() => Promise.resolve()),
    finishSignIn: vi.fn(() => {
      if (!account) return Promise.reject(new Error('no state'))
      signedIn = account
      return Promise.resolve(account)
    }),
    currentAccount: vi.fn(() => Promise.resolve(signedIn)),
    forget: vi.fn(() => {
      signedIn = null
      return Promise.resolve()
    }),
    signOut: vi.fn(() => Promise.resolve()),
  } satisfies IdentityProvider
  return provider
}

function setup({ down = false, account = OWNER as IdentityAccount | null } = {}) {
  const workspaces = server({ down })
  const provider = keycloak(account)
  const api = createIdentityProviderAuthApi({ local: createMockClient(), workspaces, provider, defaults: defaultTenant })
  return { api, workspaces, provider }
}

describe('sign-in for workspaces saved on the server', () => {
  it('sends a server workspace to Keycloak, with the email as a hint', async () => {
    const { api, provider } = setup()
    expect(await api.getWorkspaceBranding('MB-Consulting')).toMatchObject({ slug: 'mb-consulting', name: 'MB Consulting', signInFlow: 'redirect' })
    expect(await api.signIn({ workspace: 'mb-consulting', email: ' owner@mbconsulting.example ', password: '', returnTo: '/approvals' })).toEqual({ status: 'redirecting' })
    expect(provider.startSignIn).toHaveBeenCalledWith({ workspace: 'mb-consulting', email: 'owner@mbconsulting.example', returnTo: '/approvals' })
  })

  it('reopens MB Consulting from a fresh browser: the session carries the tenant and the account’s role', async () => {
    const { api } = setup()
    const { session, returnTo } = await api.completeSignIn()
    expect(returnTo).toBe('/approvals')
    expect(session.token).toBe(`${IDP_TOKEN_PREFIX}kc-sub-mb`)
    expect(session.viaIdentityProvider).toBe(true)
    expect(session.tenant).toMatchObject({ id: MB.id, slug: 'mb-consulting', name: 'MB Consulting', currency: 'INR' })
    expect(session.user).toMatchObject({ id: 'kc-sub-mb', tenantId: MB.id, role: 'hrhead', email: 'owner@mbconsulting.example', initials: 'MB' })

    // After a reload the same session comes back from Keycloak's tokens.
    expect((await api.getSession(session.token)).tenant.id).toBe(MB.id)
    expect((await api.listTeam(session.token)).map((u) => u.id)).toEqual(['kc-sub-mb'])
  })

  it('refuses an account from another tenant and forgets its tokens', async () => {
    const { api, provider } = setup({ account: { ...OWNER, tenantId: 'some-other-tenant' } })
    await expect(api.completeSignIn()).rejects.toMatchObject({ status: 403, message: expect.stringContaining('different workspace') })
    expect(provider.forget).toHaveBeenCalled()
  })

  it('refuses an account without a portal role', async () => {
    const { api } = setup({ account: { ...OWNER, roles: ['default-roles-recuro', 'tenant-admin'] } })
    await expect(api.completeSignIn()).rejects.toMatchObject({ status: 403 })
  })

  it('reports a callback that cannot be finished as 401', async () => {
    const { api } = setup({ account: null })
    await expect(api.completeSignIn()).rejects.toMatchObject({ status: 401 })
  })

  it('ends a restored session that Keycloak no longer has', async () => {
    const { api } = setup()
    await expect(api.getSession(`${IDP_TOKEN_PREFIX}kc-sub-mb`)).rejects.toMatchObject({ status: 401 })
  })

  it('signs out of Keycloak and returns to the workspace’s sign-in page', async () => {
    const { api, provider } = setup()
    const { session } = await api.completeSignIn()
    await api.signOut(session.token)
    expect(provider.signOut).toHaveBeenCalledWith('/signin?workspace=mb-consulting')
  })

  it('keeps the in-browser demo for workspaces the server doesn’t have, so personas still work', async () => {
    const { api, provider } = setup()
    expect((await api.getWorkspaceBranding('aurora')).signInFlow).toBe('password')
    const result = await api.signIn({ workspace: 'aurora', email: 'a.sharma@aurora-demo.example', password: DEMO_PASSWORD })
    expect(result.status).toBe('signedIn')
    expect(provider.startSignIn).not.toHaveBeenCalled()
  })

  it('says the server is down rather than "not found" for a workspace this browser doesn’t know', async () => {
    const { api } = setup({ down: true })
    await expect(api.getWorkspaceBranding('mb-consulting')).rejects.toMatchObject({ status: 503 })
    expect((await api.getWorkspaceBranding('aurora')).signInFlow).toBe('password')
  })
})

describe('sign-up with the server', () => {
  it('saves the workspace, then sends the owner to sign in to it', async () => {
    const { api, workspaces, provider } = setup()
    expect(await api.registerOrganisation(SIGN_UP)).toEqual({ status: 'redirecting' })
    expect(workspaces.registerWorkspace).toHaveBeenCalledWith(expect.objectContaining({ orgName: 'MB Consulting', email: 'owner@mbconsulting.example' }))
    expect(provider.startSignIn).toHaveBeenCalledWith({ workspace: 'mb-consulting', email: 'owner@mbconsulting.example' })
  })

  it('passes on the server’s refusal and does not call it for an invalid form', async () => {
    const { api, workspaces } = setup({ down: true })
    await expect(api.registerOrganisation(SIGN_UP)).rejects.toMatchObject({ status: 503 })
    vi.mocked(workspaces.registerWorkspace).mockClear()
    await expect(api.registerOrganisation({ ...SIGN_UP, password: 'weak' })).rejects.toMatchObject({ status: 400 })
    expect(workspaces.registerWorkspace).not.toHaveBeenCalled()
  })

  it('tells the owner where to sign in when Keycloak can’t be reached after the workspace is saved', async () => {
    const { api, provider } = setup()
    provider.startSignIn.mockRejectedValueOnce(new Error('metadata unreachable'))
    await expect(api.registerOrganisation(SIGN_UP)).rejects.toMatchObject({
      status: 503,
      message: expect.stringContaining('/signin?workspace=mb-consulting'),
    })
  })
})
