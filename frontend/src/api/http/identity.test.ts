import { describe, expect, it, vi } from 'vitest'
import { tenant as defaultTenant } from '../../config/tenant'
import type { HeldRole, MyAccess, Workspace } from '../../domain/types'
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
  workspaceRoles: ['hrhead'],
  accessToken: 'access-token-mb',
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

const HR_HEAD: HeldRole = { key: 'hrhead', name: 'HR Head', permissions: ['approvals.view', 'candidate.viewSensitive', 'offer.approve'], pages: ['dashboard'], requiresMfa: true }
const RECRUITER: HeldRole = { key: 'recruiter', name: 'Recruiter', permissions: ['candidate.log', 'pipeline.move'], pages: ['dashboard', 'pipeline'], requiresMfa: false }
const REVIEWER: HeldRole = { key: 'hiring-reviewer', name: 'Hiring reviewer', permissions: ['approvals.view', 'reports.view'], pages: ['approvals'], requiresMfa: false }

const accessWith = (...roles: HeldRole[]): MyAccess => ({
  workspaceId: MB.id,
  roles,
  permissions: [...new Set(roles.flatMap((r) => r.permissions))],
  pages: [...new Set(roles.flatMap((r) => r.pages))],
})

/** The admin API: knows MB Consulting only, or is down. By default the owner holds HR Head. */
function server({ down = false, accessDown = false, access = accessWith(HR_HEAD) } = {}): WorkspaceApi {
  const unreachable = () => Promise.reject(new ApiError(503, 'We couldn’t reach the Recuro server.'))
  return {
    getMyAccess: vi.fn(() => (down || accessDown ? unreachable() : Promise.resolve(access))),
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

function setup({ down = false, accessDown = false, account = OWNER as IdentityAccount | null, access = accessWith(HR_HEAD) } = {}) {
  const workspaces = server({ down, accessDown, access })
  const provider = keycloak(account)
  const api = createIdentityProviderAuthApi({ local: createMockClient(), workspaces, provider, defaults: defaultTenant })
  return { api, workspaces, provider }
}

describe('sign-in for workspaces saved on the server', () => {
  it('sends a server workspace to Keycloak, with the email as a hint', async () => {
    const { api, provider } = setup()
    expect(await api.getWorkspaceBranding('MB-Consulting')).toMatchObject({ slug: 'mb-consulting', name: 'MB Consulting', signInFlow: 'redirect' })
    expect(await api.signIn({ workspace: 'mb-consulting', email: ' owner@mbconsulting.example ', password: '', returnTo: '/approvals' })).toEqual({ status: 'redirecting' })
    expect(provider.startSignIn).toHaveBeenCalledWith({
      workspace: 'mb-consulting',
      email: 'owner@mbconsulting.example',
      returnTo: '/approvals',
      brand: expect.objectContaining({ name: 'MB Consulting' }) as unknown,
    })
  })

  it('finds the workspace from its name as people type it, spaces and capitals included', async () => {
    const { api } = setup()
    expect(await api.getWorkspaceBranding('  MB Consulting ')).toMatchObject({ slug: 'mb-consulting', signInFlow: 'redirect' })
  })

  it('reopens MB Consulting from a fresh browser: the session carries the workspace and the account’s role', async () => {
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

  it('refuses an account from another workspace and forgets its tokens', async () => {
    const { api, provider } = setup({ account: { ...OWNER, tenantId: 'some-other-tenant' } })
    await expect(api.completeSignIn()).rejects.toMatchObject({ status: 403, message: expect.stringContaining('different workspace') })
    expect(provider.forget).toHaveBeenCalled()
  })

  it('refuses an account the workspace gave no role the portal can show', async () => {
    const { api } = setup({ access: accessWith() })
    await expect(api.completeSignIn()).rejects.toMatchObject({ status: 403, message: expect.stringContaining('no Recuro role') })
  })

  it('signs in someone whose only role is one the workspace created, with that role’s permissions', async () => {
    const recruiter = { ...OWNER, roles: ['default-roles-recuro'], workspaceRoles: ['recruiter'] }
    const { api, workspaces } = setup({ account: recruiter, access: accessWith(RECRUITER) })
    const { session } = await api.completeSignIn()
    expect(workspaces.getMyAccess).toHaveBeenCalledWith('access-token-mb')
    expect(session.user).toMatchObject({ role: 'hrta', tenantId: MB.id, permissions: ['candidate.log', 'pipeline.move'] })
  })

  it('gives an approver role without access to candidate details the masked view', async () => {
    const { api } = setup({ account: { ...OWNER, roles: [], workspaceRoles: ['hiring-reviewer'] }, access: accessWith(REVIEWER) })
    expect((await api.completeSignIn()).session.user.role).toBe('mdceo')
  })

  it('goes by the workspace’s assignments, not stale realm roles in the token', async () => {
    const { api } = setup({ access: accessWith(RECRUITER) })
    expect((await api.completeSignIn()).session.user.role).toBe('hrta')
  })

  it('falls back to the token’s role keys when the admin API can’t say', async () => {
    expect((await setup({ accessDown: true }).api.completeSignIn()).session.user.role).toBe('hrhead')
    const custom = setup({ accessDown: true, account: { ...OWNER, roles: [], workspaceRoles: ['recruiter'] } })
    await expect(custom.api.completeSignIn()).rejects.toMatchObject({ status: 403 })
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
    expect(provider.startSignIn).toHaveBeenCalledWith({ workspace: 'mb-consulting', email: 'owner@mbconsulting.example', brand: expect.objectContaining({ name: 'MB Consulting' }) as unknown })
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
