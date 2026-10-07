import type { AuthSession, SignInResult } from '../../domain/types'
import { ApiError } from '../contract'
import { createMockClient } from './mockClient'
import { DEMO_PASSWORD } from './seed'

const WS = 'aurora'
const HRTA = 'a.sharma@aurora-demo.example'
const HRHEAD = 'k.iyer@aurora-demo.example'
const STRONG = 'N3w-password!'

async function expectStatus(p: Promise<unknown>, status: number) {
  await expect(p).rejects.toBeInstanceOf(ApiError)
  await expect(p).rejects.toMatchObject({ status })
}

function signedIn(result: SignInResult): AuthSession {
  if (result.status !== 'signedIn') throw new Error(`expected a session, got ${result.status}`)
  return result.session
}

describe('identity mock (RCU-PLT-001)', () => {
  it('signs in with email and password, and restores then ends the session', async () => {
    const api = createMockClient()
    const session = signedIn(await api.signIn({ workspace: WS, email: ' A.Sharma@Aurora-Demo.example ', password: DEMO_PASSWORD }))
    expect(session.user.role).toBe('hrta')
    expect(session.tenant.slug).toBe(WS)
    expect((await api.getSession(session.token)).user.id).toBe(session.user.id)
    await api.signOut(session.token)
    await expectStatus(api.getSession(session.token), 401)
    expect((await api.listAudit()).map((e) => e.action)).toEqual(expect.arrayContaining(['SIGN_IN', 'SIGN_OUT']))
  })

  it('gives the same answer for an unknown email and a wrong password', async () => {
    const api = createMockClient()
    const unknown = api.signIn({ workspace: WS, email: 'nobody@aurora-demo.example', password: DEMO_PASSWORD })
    const wrong = api.signIn({ workspace: WS, email: HRTA, password: 'nope' })
    await expectStatus(unknown, 401)
    await expectStatus(wrong, 401)
    expect((await unknown.catch((e: Error) => e.message))).toBe(await wrong.catch((e: Error) => e.message))
  })

  it('locks the account after 5 failed attempts, even for the right password', async () => {
    const api = createMockClient()
    for (let i = 0; i < 4; i++) await expectStatus(api.signIn({ workspace: WS, email: HRTA, password: 'wrong' }), 401)
    await expectStatus(api.signIn({ workspace: WS, email: HRTA, password: 'wrong' }), 423)
    await expectStatus(api.signIn({ workspace: WS, email: HRTA, password: DEMO_PASSWORD }), 423)
    expect((await api.listAudit()).some((e) => e.action === 'ACCOUNT_LOCKED')).toBe(true)
  })

  it('asks elevated roles for a second factor', async () => {
    const api = createMockClient()
    const result = await api.signIn({ workspace: WS, email: HRHEAD, password: DEMO_PASSWORD })
    if (result.status !== 'mfaRequired') throw new Error('expected MFA')
    expect(result.deliveredTo).toBe('k.***@aurora-demo.example')
    await expectStatus(api.verifyMfa(result.challengeId, '000000' === result.demoCode ? '111111' : '000000'), 401)
    const session = await api.verifyMfa(result.challengeId, result.demoCode ?? '')
    expect(session.user.role).toBe('hrhead')
    await expectStatus(api.verifyMfa(result.challengeId, result.demoCode ?? ''), 401)
  })

  it('rejects unknown workspaces and reports SSO as not connected', async () => {
    const api = createMockClient()
    await expectStatus(api.getWorkspaceBranding('nope'), 404)
    await expectStatus(api.signInWithSso(WS, 'microsoft'), 501)
  })

  it('lets any kind of organisation create a workspace with the same features', async () => {
    const api = createMockClient()
    const owner = await api.registerOrganisation({
      orgName: 'Northwind Talent Partners',
      orgType: 'agency',
      adminName: 'J. Doe',
      adminRole: 'hrhead',
      email: 'j.doe@northwind.example',
      password: STRONG,
      acceptTerms: true,
    })
    expect(owner.tenant).toMatchObject({ slug: 'northwind-talent-partners', orgType: 'agency', name: 'Northwind Talent Partners' })
    expect(owner.user.tenantId).toBe(owner.tenant.id)
    const second = await api.registerOrganisation({
      orgName: 'Northwind Talent Partners',
      orgType: 'smallBusiness',
      adminName: 'Other',
      adminRole: 'hrta',
      email: 'o@other.example',
      password: STRONG,
      acceptTerms: true,
    })
    expect(second.tenant.slug).toBe('northwind-talent-partners-2')
    expect((await api.getWorkspaceBranding('northwind-talent-partners')).orgType).toBe('agency')
    await expectStatus(api.registerOrganisation({ ...{ orgName: 'X', orgType: 'enterprise', adminName: 'Y', adminRole: 'hrhead', email: 'y@x.example', acceptTerms: true }, password: 'weak' }), 400)
  })

  it('registers candidates per workspace and refuses duplicates', async () => {
    const api = createMockClient()
    const input = { workspace: WS, name: 'Priya N', email: 'priya@mail.example', password: STRONG, privacyConsent: true }
    const session = await api.registerCandidate(input)
    expect(session.user.role).toBe('candidate')
    await expectStatus(api.registerCandidate(input), 409)
    await expectStatus(api.registerCandidate({ ...input, email: 'p2@mail.example', privacyConsent: false }), 400)
  })

  it('resets a password with a single-use link and ends existing sessions', async () => {
    const api = createMockClient()
    const before = signedIn(await api.signIn({ workspace: WS, email: HRTA, password: DEMO_PASSWORD }))
    const unknown = await api.requestPasswordReset(WS, 'ghost@aurora-demo.example')
    expect(unknown.demoResetPath).toBeUndefined()
    const sent = await api.requestPasswordReset(WS, HRTA)
    const token = new URLSearchParams(sent.demoResetPath?.split('?')[1]).get('token') ?? ''
    await api.resetPassword(token, STRONG)
    await expectStatus(api.resetPassword(token, STRONG), 401)
    await expectStatus(api.getSession(before.token), 401)
    await expectStatus(api.signIn({ workspace: WS, email: HRTA, password: DEMO_PASSWORD }), 401)
    expect(signedIn(await api.signIn({ workspace: WS, email: HRTA, password: STRONG })).user.role).toBe('hrta')
  })

  it('lets HR invite staff, who join with their own password', async () => {
    const api = createMockClient()
    const admin = signedIn(await api.signIn({ workspace: WS, email: HRTA, password: DEMO_PASSWORD }))
    const invite = await api.inviteStaff(admin.token, { name: 'M. Rao', email: 'm.rao@aurora-demo.example', role: 'employee' })
    expect(invite).not.toHaveProperty('token')
    await expectStatus(api.inviteStaff(admin.token, { name: 'M. Rao', email: 'M.Rao@aurora-demo.example', role: 'employee' }), 409)

    const token = invite.acceptPath.split('/').pop() ?? ''
    expect((await api.getInvitation(token)).workspace.slug).toBe(WS)
    const joined = await api.acceptInvitation({ token, name: 'M. Rao', password: STRONG })
    expect(joined.user).toMatchObject({ role: 'employee', tenantId: admin.tenant.id })
    await expectStatus(api.getInvitation(token), 401)
    expect((await api.listTeam(admin.token)).some((u) => u.email === 'm.rao@aurora-demo.example')).toBe(true)
    expect((await api.listInvitations(admin.token))[0]?.status).toBe('Accepted')

    await expectStatus(api.inviteStaff(joined.token, { name: 'Z', email: 'z@aurora-demo.example', role: 'hrta' }), 403)
  })

  it('keeps accounts it created after the page reloads', async () => {
    const first = createMockClient()
    await first.registerCandidate({ workspace: WS, name: 'Kept', email: 'kept@mail.example', password: STRONG, privacyConsent: true })
    const reloaded = createMockClient()
    expect(signedIn(await reloaded.signIn({ workspace: WS, email: 'kept@mail.example', password: STRONG })).user.name).toBe('Kept')
  })
})
