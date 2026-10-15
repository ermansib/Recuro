import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../contract'
import { ACCESS_REFUSED, LOOKUP_SERVER_DOWN, SIGN_UP_SERVER_DOWN, createHttpWorkspaceApi } from './workspaces'

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

  it('reports a dead admin API behind the dev proxy as unreachable', async () => {
    const api = createHttpWorkspaceApi('', vi.fn().mockResolvedValue(new Response('', { status: 500 })))
    await expect(api.registerWorkspace(INPUT)).rejects.toMatchObject({ status: 503, message: SIGN_UP_SERVER_DOWN })
  })

  it('reports an unreachable server as 503', async () => {
    const api = createHttpWorkspaceApi('', vi.fn().mockRejectedValue(new TypeError('fetch failed')))
    await expect(api.registerWorkspace(INPUT)).rejects.toBeInstanceOf(ApiError)
    await expect(api.registerWorkspace(INPUT)).rejects.toMatchObject({ status: 503 })
  })
})

describe('http workspace lookup', () => {
  it('returns the workspace, or null when the server has none', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(json(SAVED.workspace)).mockResolvedValueOnce(json({ title: 'Not found' }, 404))
    const api = createHttpWorkspaceApi('', fetcher)
    expect(await api.getWorkspace('acme-corp-2')).toEqual(SAVED.workspace)
    expect(fetcher).toHaveBeenCalledWith('/api/v1/workspaces/acme-corp-2', expect.objectContaining({ method: 'GET' }))
    expect(await api.getWorkspace('nope')).toBeNull()
  })

  it('reports an unreachable server, or a dev server without the proxy, as 503', async () => {
    const down = createHttpWorkspaceApi('', vi.fn().mockRejectedValue(new TypeError('fetch failed')))
    await expect(down.getWorkspace('acme')).rejects.toMatchObject({ status: 503, message: LOOKUP_SERVER_DOWN })
    const html = createHttpWorkspaceApi('', vi.fn().mockResolvedValue(new Response('<!doctype html>', { status: 200 })))
    await expect(html.getWorkspace('acme')).rejects.toMatchObject({ status: 503 })
  })
})

describe('http access of the signed-in person', () => {
  it('sends the person’s token and reads their roles', async () => {
    const access = { workspaceId: SAVED.workspace.id, roles: [], permissions: [], pages: [] }
    const fetcher = vi.fn().mockResolvedValue(json(access))
    expect(await createHttpWorkspaceApi('', fetcher).getMyAccess('tok')).toEqual(access)
    expect(fetcher).toHaveBeenCalledWith('/api/v1/me/access', expect.objectContaining({ headers: expect.objectContaining({ Authorization: 'Bearer tok' }) }))
  })

  it('passes on a refusal and reports an unreachable server as 503', async () => {
    await expect(createHttpWorkspaceApi('', vi.fn().mockResolvedValue(json({ title: 'Forbidden' }, 403))).getMyAccess('tok')).rejects.toMatchObject({ status: 403 })
    await expect(createHttpWorkspaceApi('', vi.fn().mockRejectedValue(new TypeError('down'))).getMyAccess('tok')).rejects.toMatchObject({ status: 503 })
  })

  it('explains a refused token instead of showing a bare "Unauthorized"', async () => {
    const fetcher = vi.fn().mockResolvedValue(json({ title: 'Unauthorized', status: 401 }, 401))
    await expect(createHttpWorkspaceApi('', fetcher).getMyAccess('tok')).rejects.toMatchObject({ status: 401, message: ACCESS_REFUSED })
  })
})
