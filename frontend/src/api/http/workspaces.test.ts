import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../contract'
import { createMockClient } from '../mock/mockClient'
import { createHttpWorkspaceApi } from './workspaces'

const INPUT = {
  orgName: 'Acme Corp',
  orgType: 'agency' as const,
  adminName: 'Asha Rao',
  adminRole: 'hrhead' as const,
  email: 'Asha@Acme.example',
  password: 'Str0ng!pass',
  acceptTerms: true,
}

const SAVED = {
  workspace: {
    id: '0b6f4d0e-6a0c-4a5d-8d1d-6a6a2f0a1111',
    slug: 'acme-corp-2',
    name: 'Acme Corp',
    orgType: 'agency',
    careersTagline: 'Find your next role through us',
    emailDomain: 'acme.example',
    locale: 'en-IN',
    currency: 'INR',
    ssoProviders: ['google', 'microsoft'],
    mfaRoles: ['hrhead', 'mdceo'],
    sessionIdleMinutes: 30,
  },
  owner: { id: 'kc-sub-1', name: 'Asha Rao', email: 'asha@acme.example', role: 'hrhead' },
}

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

describe('http workspace api', () => {
  it('posts the sign-up form to the admin API', async () => {
    const fetcher = vi.fn().mockResolvedValue(json(SAVED, 201))
    const api = createHttpWorkspaceApi('', fetcher)
    expect(await api.registerWorkspace(INPUT)).toEqual(SAVED)
    expect(fetcher).toHaveBeenCalledWith('/api/v1/workspaces', expect.objectContaining({ method: 'POST', body: JSON.stringify(INPUT) }))
  })

  it('surfaces the server’s problem title and status', async () => {
    const api = createHttpWorkspaceApi('', vi.fn().mockResolvedValue(json({ title: 'An account with this email already exists.' }, 409)))
    await expect(api.registerWorkspace(INPUT)).rejects.toMatchObject({ status: 409, message: 'An account with this email already exists.' })
  })

  it('reports an unreachable server as 503', async () => {
    const api = createHttpWorkspaceApi('', vi.fn().mockRejectedValue(new TypeError('fetch failed')))
    await expect(api.registerWorkspace(INPUT)).rejects.toBeInstanceOf(ApiError)
    await expect(api.registerWorkspace(INPUT)).rejects.toMatchObject({ status: 503 })
  })
})

describe('sign-up with a server', () => {
  it('uses the workspace the server saved, including its slug and ids', async () => {
    const registerWorkspace = vi.fn().mockResolvedValue(SAVED)
    const api = createMockClient(undefined, { registerWorkspace })
    const session = await api.registerOrganisation(INPUT)
    expect(registerWorkspace).toHaveBeenCalledWith(expect.objectContaining({ orgName: 'Acme Corp', email: 'asha@acme.example' }))
    expect(session.tenant).toMatchObject({ id: SAVED.workspace.id, slug: 'acme-corp-2', emailDomain: 'acme.example' })
    expect(session.user).toMatchObject({ id: 'kc-sub-1', tenantId: SAVED.workspace.id, role: 'hrhead' })
  })

  it('creates nothing locally when the server refuses', async () => {
    const registerWorkspace = vi.fn().mockRejectedValue(new ApiError(409, 'An account with this email already exists.'))
    const api = createMockClient(undefined, { registerWorkspace })
    await expect(api.registerOrganisation(INPUT)).rejects.toMatchObject({ status: 409 })
    await expect(api.getWorkspaceBranding('acme-corp')).rejects.toMatchObject({ status: 404 })
  })

  it('does not call the server when the form is invalid', async () => {
    const registerWorkspace = vi.fn()
    const api = createMockClient(undefined, { registerWorkspace })
    await expect(api.registerOrganisation({ ...INPUT, password: 'weak' })).rejects.toMatchObject({ status: 400 })
    expect(registerWorkspace).not.toHaveBeenCalled()
  })
})
