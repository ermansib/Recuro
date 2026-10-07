// In-memory stand-in for the .NET identity service (RCU-PLT-001). It applies the same rules the
// service must: password policy, lockout after repeated failures, MFA for elevated roles,
// single-use reset and invite links, and tenant isolation of accounts.
//
// Unlike the rest of the mock, identity state is kept in localStorage so that a workspace or
// account created in the demo survives a page reload. Passwords are stored only as SHA-256
// hashes; the real service will use ASP.NET Core Identity's salted hashing.
import {
  INVITE_VALID_DAYS,
  LOCKOUT_MINUTES,
  MAX_FAILED_SIGN_INS,
  MFA_CODE_LENGTH,
  ORG_TYPE_DEFAULTS,
  RESET_LINK_VALID_MINUTES,
  isValidEmail,
  maskEmail,
  normaliseEmail,
  requiresMfa,
  slugify,
  unmetPasswordRules,
} from '../../domain/auth'
import type {
  AuthSession,
  Invitation,
  Role,
  SignInResult,
  TenantConfig,
  User,
  WorkspaceBranding,
} from '../../domain/types'
import { can } from '../../auth/permissions'
import { ApiError, type Actor, type AuthApi } from '../contract'
import type { MockDb } from './seed'

const STORAGE_KEY = 'recuro.mock.identity.v1'
const MFA_VALID_MINUTES = 5
const MINUTE_MS = 60_000
const DAY_MS = 24 * 60 * MINUTE_MS

interface Credential {
  passwordHash: string
  failedAttempts: number
  lockedUntil: string | null
}

interface IdentityStore {
  tenants: TenantConfig[]
  users: User[]
  credentials: Record<string, Credential>
  sessions: Record<string, { userId: string; issuedAt: string }>
  invitations: (Invitation & { token: string })[]
  resetTokens: Record<string, { userId: string; expiresAt: string }>
  mfaChallenges: Record<string, { userId: string; code: string; expiresAt: string }>
}

/** Helpers the identity mock borrows from the main mock so audit and email stay in one place. */
export interface IdentityDeps {
  db: MockDb
  demoPassword: string
  audit: (actor: Actor, entity: string, action: string, extra?: { reason?: string; before?: string; after?: string }) => void
  sendEmail: (role: Role, to: string, tenant: TenantConfig, subject: string, paragraphs: string[], cta: string) => void
  run: <T>(fn: () => T | Promise<T>) => Promise<T>
}

export async function hashPassword(password: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(password))
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, '0')).join('')
}

function readStore(): IdentityStore | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return raw ? (JSON.parse(raw) as IdentityStore) : null
  } catch {
    return null
  }
}

function writeStore(store: IdentityStore): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(store))
  } catch {
    // Storage can be unavailable (private mode); identity then lasts for this page only.
  }
}

const newToken = () => crypto.randomUUID().replaceAll('-', '')
const numericCode = () =>
  String(crypto.getRandomValues(new Uint32Array(1))[0] ?? 0)
    .padStart(MFA_CODE_LENGTH, '0')
    .slice(-MFA_CODE_LENGTH)
const initialsOf = (name: string) =>
  name
    .split(/[\s.]+/)
    .filter(Boolean)
    .map((p) => p[0]?.toUpperCase() ?? '')
    .join('')
    .slice(0, 2) || '?'

export function brandingOf(t: TenantConfig): WorkspaceBranding {
  return {
    slug: t.slug,
    name: t.name,
    orgType: t.orgType,
    careersTagline: t.careersTagline,
    theme: t.theme,
    ssoProviders: t.ssoProviders,
    sessionIdleMinutes: t.sessionIdleMinutes,
  }
}

export function createIdentityMock({ db, demoPassword, audit, sendEmail, run }: IdentityDeps): AuthApi {
  let store: IdentityStore | null = null

  async function load(): Promise<IdentityStore> {
    if (store) return store
    const saved = readStore()
    if (saved) return (store = saved)
    const demoHash = await hashPassword(demoPassword)
    store = {
      tenants: [db.tenant],
      users: db.users,
      credentials: Object.fromEntries(db.users.map((u) => [u.id, { passwordHash: demoHash, failedAttempts: 0, lockedUntil: null }])),
      sessions: {},
      invitations: [],
      resetTokens: {},
      mfaChallenges: {},
    }
    writeStore(store)
    return store
  }

  /** Runs a write against the store and persists it, even when the call ends in an error. */
  const mutate = <T>(fn: (s: IdentityStore) => Promise<T> | T) =>
    run(async () => {
      const s = await load()
      try {
        return await fn(s)
      } finally {
        writeStore(s)
      }
    })

  const now = () => new Date()
  const asActor = (u: User): Actor => ({ name: u.name, role: u.role })

  function tenantBySlug(s: IdentityStore, workspace: string): TenantConfig {
    const tenant = s.tenants.find((t) => t.slug === workspace.trim().toLowerCase())
    if (!tenant) throw new ApiError(404, 'We couldn’t find that workspace. Check the name with your administrator.')
    return tenant
  }

  function tenantOf(s: IdentityStore, user: User): TenantConfig {
    const tenant = s.tenants.find((t) => t.id === user.tenantId)
    if (!tenant) throw new ApiError(404, 'Workspace not found')
    return tenant
  }

  function findAccount(s: IdentityStore, tenantId: string, email: string): User | undefined {
    const wanted = normaliseEmail(email)
    return s.users.find((u) => u.tenantId === tenantId && normaliseEmail(u.email) === wanted)
  }

  function openSession(s: IdentityStore, user: User): AuthSession {
    const token = newToken()
    const issuedAt = now().toISOString()
    s.sessions[token] = { userId: user.id, issuedAt }
    audit(asActor(user), `User/${user.id}`, 'SIGN_IN')
    return { token, user, tenant: tenantOf(s, user), issuedAt }
  }

  function sessionUser(s: IdentityStore, token: string): User {
    const entry = s.sessions[token]
    const user = entry && s.users.find((u) => u.id === entry.userId)
    if (!user) throw new ApiError(401, 'Your session has ended. Please sign in again.')
    return user
  }

  function requirePassword(password: string) {
    if (unmetPasswordRules(password).length > 0) throw new ApiError(400, 'Password does not meet the policy')
  }

  function requireEmail(email: string) {
    if (!isValidEmail(email)) throw new ApiError(400, 'Enter a valid email address')
  }

  async function createAccount(s: IdentityStore, user: Omit<User, 'id' | 'initials'>, password: string): Promise<User> {
    const created: User = { ...user, id: `u-${newToken().slice(0, 10)}`, initials: initialsOf(user.name) }
    s.users.push(created)
    s.credentials[created.id] = { passwordHash: await hashPassword(password), failedAttempts: 0, lockedUntil: null }
    return created
  }

  function uniqueSlug(s: IdentityStore, name: string): string {
    const base = slugify(name) || 'workspace'
    let slug = base
    for (let n = 2; s.tenants.some((t) => t.slug === slug); n++) slug = `${base}-${n}`
    return slug
  }

  function signedInOrMfa(s: IdentityStore, user: User): SignInResult {
    const tenant = tenantOf(s, user)
    if (!requiresMfa(tenant, user.role)) return { status: 'signedIn', session: openSession(s, user) }
    const challengeId = newToken()
    const code = numericCode()
    s.mfaChallenges[challengeId] = {
      userId: user.id,
      code,
      expiresAt: new Date(now().getTime() + MFA_VALID_MINUTES * MINUTE_MS).toISOString(),
    }
    sendEmail(user.role, user.email, tenant, `Your ${tenant.name} sign-in code`, [
      `Your verification code is ${code}. It expires in ${MFA_VALID_MINUTES} minutes.`,
      'If you didn’t try to sign in, change your password.',
    ], 'Verification code')
    return { status: 'mfaRequired', challengeId, deliveredTo: maskEmail(user.email), demoCode: code }
  }

  return {
    getWorkspaceBranding: (workspace) => run(async () => brandingOf(tenantBySlug(await load(), workspace))),

    signIn: ({ workspace, email, password }) =>
      mutate(async (s) => {
        const tenant = tenantBySlug(s, workspace)
        const user = findAccount(s, tenant.id, email)
        const cred = user && s.credentials[user.id]
        // Same message whether the account exists or not, so sign-in can't be used to discover accounts.
        const invalid = new ApiError(401, 'That email and password don’t match. Check them and try again.')
        if (!user || !cred) throw invalid
        if (cred.lockedUntil && new Date(cred.lockedUntil) > now()) {
          throw new ApiError(423, `This account is locked after ${MAX_FAILED_SIGN_INS} failed attempts. Try again in ${LOCKOUT_MINUTES} minutes or reset your password.`)
        }
        if ((await hashPassword(password)) !== cred.passwordHash) {
          cred.failedAttempts += 1
          audit(asActor(user), `User/${user.id}`, 'SIGN_IN_FAILED', { after: `${cred.failedAttempts}/${MAX_FAILED_SIGN_INS}` })
          if (cred.failedAttempts >= MAX_FAILED_SIGN_INS) {
            cred.lockedUntil = new Date(now().getTime() + LOCKOUT_MINUTES * MINUTE_MS).toISOString()
            cred.failedAttempts = 0
            audit(asActor(user), `User/${user.id}`, 'ACCOUNT_LOCKED')
            throw new ApiError(423, `This account is locked after ${MAX_FAILED_SIGN_INS} failed attempts. Try again in ${LOCKOUT_MINUTES} minutes or reset your password.`)
          }
          throw invalid
        }
        cred.failedAttempts = 0
        cred.lockedUntil = null
        return signedInOrMfa(s, user)
      }),

    verifyMfa: (challengeId, code) =>
      mutate((s) => {
        const challenge = s.mfaChallenges[challengeId]
        const user = challenge && s.users.find((u) => u.id === challenge.userId)
        if (!challenge || !user || new Date(challenge.expiresAt) < now()) {
          throw new ApiError(401, 'That code has expired. Sign in again to get a new one.')
        }
        if (challenge.code !== code.trim()) throw new ApiError(401, 'That code isn’t right. Check the latest email and try again.')
        delete s.mfaChallenges[challengeId]
        return openSession(s, user)
      }),

    signInWithSso: (workspace, provider) =>
      run(async () => {
        const tenant = tenantBySlug(await load(), workspace)
        if (!tenant.ssoProviders.includes(provider)) throw new ApiError(400, 'This sign-in option is not enabled for the workspace')
        throw new ApiError(501, 'Single sign-on is not connected in this demo. Use your email and password.')
      }),

    getSession: (token) =>
      run(async () => {
        const s = await load()
        const user = sessionUser(s, token)
        const entry = s.sessions[token]
        return { token, user, tenant: tenantOf(s, user), issuedAt: entry?.issuedAt ?? now().toISOString() }
      }),

    signOut: (token) =>
      mutate((s) => {
        const entry = s.sessions[token]
        const user = entry && s.users.find((u) => u.id === entry.userId)
        if (user) audit(asActor(user), `User/${user.id}`, 'SIGN_OUT')
        delete s.sessions[token]
      }),

    registerOrganisation: (input) =>
      mutate(async (s) => {
        if (!input.orgName.trim() || !input.adminName.trim()) throw new ApiError(400, 'Organisation and your name are required')
        requireEmail(input.email)
        requirePassword(input.password)
        if (!input.acceptTerms) throw new ApiError(400, 'Accept the terms to create a workspace')
        const defaults = ORG_TYPE_DEFAULTS[input.orgType]
        const email = normaliseEmail(input.email)
        const tenant: TenantConfig = {
          ...db.tenant,
          id: `tnt-${newToken().slice(0, 10)}`,
          slug: uniqueSlug(s, input.orgName),
          name: input.orgName.trim(),
          legalName: input.orgName.trim(),
          orgType: input.orgType,
          careersTagline: defaults.careersTagline,
          careersIntro: '',
          emailDomain: email.split('@')[1] ?? db.tenant.emailDomain,
          theme: undefined,
          ssoProviders: defaults.ssoProviders,
          mfaRoles: defaults.mfaRoles,
        }
        s.tenants.push(tenant)
        const owner = await createAccount(
          s,
          { tenantId: tenant.id, name: input.adminName.trim(), role: input.adminRole, title: 'Workspace owner', email, summary: '' },
          input.password,
        )
        audit(asActor(owner), `Tenant/${tenant.id}`, 'WORKSPACE_CREATED', { after: input.orgType })
        return openSession(s, owner)
      }),

    registerCandidate: (input) =>
      mutate(async (s) => {
        const tenant = tenantBySlug(s, input.workspace)
        if (!input.name.trim()) throw new ApiError(400, 'Your name is required')
        requireEmail(input.email)
        requirePassword(input.password)
        if (!input.privacyConsent) throw new ApiError(400, 'Consent to the privacy notice to create an account')
        if (findAccount(s, tenant.id, input.email)) {
          throw new ApiError(409, 'An account with this email already exists. Sign in or reset your password.')
        }
        const candidate = await createAccount(
          s,
          { tenantId: tenant.id, name: input.name.trim(), role: 'candidate', title: 'Candidate', email: normaliseEmail(input.email), summary: '' },
          input.password,
        )
        return openSession(s, candidate)
      }),

    requestPasswordReset: (workspace, email) =>
      mutate((s) => {
        requireEmail(email)
        const tenant = tenantBySlug(s, workspace)
        const user = findAccount(s, tenant.id, email)
        const deliveredTo = maskEmail(normaliseEmail(email))
        // Respond the same way for unknown emails so the form can't be used to find accounts.
        if (!user) return { deliveredTo }
        const token = newToken()
        s.resetTokens[token] = { userId: user.id, expiresAt: new Date(now().getTime() + RESET_LINK_VALID_MINUTES * MINUTE_MS).toISOString() }
        const path = `/reset-password?token=${token}`
        sendEmail(user.role, user.email, tenant, `Reset your ${tenant.name} password`, [
          `Use the link below to choose a new password. It works once and expires in ${RESET_LINK_VALID_MINUTES} minutes.`,
          'If you didn’t ask for this, you can ignore this email.',
        ], 'Reset password')
        return { deliveredTo, demoResetPath: path }
      }),

    resetPassword: (token, password) =>
      mutate(async (s) => {
        const entry = s.resetTokens[token]
        const user = entry && s.users.find((u) => u.id === entry.userId)
        if (!entry || !user || new Date(entry.expiresAt) < now()) {
          throw new ApiError(401, 'This reset link has expired or was already used. Ask for a new one.')
        }
        requirePassword(password)
        s.credentials[user.id] = { passwordHash: await hashPassword(password), failedAttempts: 0, lockedUntil: null }
        delete s.resetTokens[token]
        // A new password ends every existing session for the account.
        for (const [t, sess] of Object.entries(s.sessions)) if (sess.userId === user.id) delete s.sessions[t]
        audit(asActor(user), `User/${user.id}`, 'PASSWORD_RESET')
      }),

    listTeam: (token) =>
      run(async () => {
        const s = await load()
        const me = sessionUser(s, token)
        return s.users.filter((u) => u.tenantId === me.tenantId && u.role !== 'candidate')
      }),

    listInvitations: (token) =>
      run(async () => {
        const s = await load()
        const me = sessionUser(s, token)
        return s.invitations
          .filter((i) => i.tenantId === me.tenantId)
          .map(({ token: _secret, ...invite }) => ({
            ...invite,
            status: invite.status === 'Pending' && new Date(invite.expiresAt) < now() ? 'Expired' : invite.status,
          }))
      }),

    inviteStaff: (token, input) =>
      mutate((s) => {
        const me = sessionUser(s, token)
        if (!can(me.role, 'team.invite')) {
          audit(asActor(me), 'access', 'ACCESS_DENIED', { reason: 'team.invite' })
          throw new ApiError(403, 'Only HR administrators can invite people')
        }
        if (!input.name.trim()) throw new ApiError(400, 'Name is required')
        requireEmail(input.email)
        if (findAccount(s, me.tenantId, input.email)) throw new ApiError(409, 'This person already has an account in the workspace')
        if (s.invitations.some((i) => i.tenantId === me.tenantId && i.status === 'Pending' && normaliseEmail(i.email) === normaliseEmail(input.email))) {
          throw new ApiError(409, 'This person already has a pending invitation')
        }
        const tenant = tenantOf(s, me)
        const secret = newToken()
        const invite: Invitation & { token: string } = {
          id: `inv-${secret.slice(0, 8)}`,
          token: secret,
          tenantId: me.tenantId,
          name: input.name.trim(),
          email: normaliseEmail(input.email),
          role: input.role,
          invitedBy: me.name,
          createdAt: now().toISOString(),
          expiresAt: new Date(now().getTime() + INVITE_VALID_DAYS * DAY_MS).toISOString(),
          status: 'Pending',
          acceptPath: `/invite/${secret}`,
        }
        s.invitations.unshift(invite)
        sendEmail(input.role, invite.email, tenant, `${me.name} invited you to ${tenant.name} on Recuro`, [
          `Hi ${invite.name}, you’ve been invited to join ${tenant.name}’s recruitment workspace.`,
          `Set your password within ${INVITE_VALID_DAYS} days using the link below.`,
        ], 'Accept invitation')
        audit(asActor(me), `Invitation/${invite.id}`, 'INVITE_SENT', { after: input.role })
        const { token: _secret, ...view } = invite
        return view
      }),

    getInvitation: (inviteToken) =>
      run(async () => {
        const s = await load()
        const invite = s.invitations.find((i) => i.token === inviteToken)
        if (!invite || invite.status !== 'Pending' || new Date(invite.expiresAt) < now()) {
          throw new ApiError(401, 'This invitation has expired or was already used. Ask your administrator for a new one.')
        }
        const tenant = s.tenants.find((t) => t.id === invite.tenantId)
        if (!tenant) throw new ApiError(404, 'Workspace not found')
        return { workspace: brandingOf(tenant), name: invite.name, email: invite.email, role: invite.role, invitedBy: invite.invitedBy }
      }),

    acceptInvitation: ({ token, name, password }) =>
      mutate(async (s) => {
        const invite = s.invitations.find((i) => i.token === token)
        if (!invite || invite.status !== 'Pending' || new Date(invite.expiresAt) < now()) {
          throw new ApiError(401, 'This invitation has expired or was already used. Ask your administrator for a new one.')
        }
        requirePassword(password)
        if (findAccount(s, invite.tenantId, invite.email)) throw new ApiError(409, 'An account with this email already exists. Sign in instead.')
        const user = await createAccount(
          s,
          { tenantId: invite.tenantId, name: name.trim() || invite.name, role: invite.role, title: '', email: invite.email, summary: '' },
          password,
        )
        invite.status = 'Accepted'
        audit(asActor(user), `Invitation/${invite.id}`, 'INVITE_ACCEPTED')
        return openSession(s, user)
      }),

    getDemoAccess: (workspace) =>
      run(async () => {
        const s = await load()
        const tenant = tenantBySlug(s, workspace)
        if (tenant.id !== db.tenant.id) throw new ApiError(404, 'No demo personas for this workspace')
        return { personas: db.users, password: demoPassword }
      }),

    demoSignIn: (workspace, role) =>
      mutate((s) => {
        const tenant = tenantBySlug(s, workspace)
        const user = tenant.id === db.tenant.id ? s.users.find((u) => u.tenantId === tenant.id && u.role === role && db.users.some((d) => d.id === u.id)) : undefined
        if (!user) throw new ApiError(404, 'No demo persona for this role')
        return openSession(s, user)
      }),
  }
}
