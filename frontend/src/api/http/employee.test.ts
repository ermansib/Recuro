import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../contract'
import { EMPLOYEE_SERVER_DOWN, createHttpEmployeeApi } from './employee'

const reply = (status: number, body: unknown) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))
const input = { reqId: 'REQ-1', name: ' Ravi ', email: 'ravi@x.example', phone: '', experienceYears: 4, relationship: 'Ex-colleague', coiAccepted: true }

describe('createHttpEmployeeApi', () => {
  it('lists internal jobs with the person’s own access token', async () => {
    const fetcher = vi.fn().mockReturnValue(reply(200, [{ reqId: 'REQ-1', title: 'Analyst' }]))
    const api = createHttpEmployeeApi('', async () => 'tok', fetcher)
    expect(await api.listInternalJobs('idp:1')).toHaveLength(1)
    const [url, init] = fetcher.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/v1/employee/ijp')
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer tok')
  })

  it('posts a referral with the COI declaration and trims the fields', async () => {
    const fetcher = vi.fn().mockReturnValue(reply(201, { id: 'r1' }))
    await createHttpEmployeeApi('', async () => 'tok', fetcher).submitReferral('idp:1', input)
    const [url, init] = fetcher.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/v1/employee/referrals')
    expect(JSON.parse(init.body as string)).toMatchObject({ name: 'Ravi', phone: null, coiAccepted: true })
  })

  it('maps 401, 403, a dead gateway and problem responses', async () => {
    const run = (r: Promise<Response>) => createHttpEmployeeApi('', async () => 'tok', () => r).listMyReferrals('idp:1').catch((e: ApiError) => e)
    expect(((await run(reply(401, {}))) as ApiError).status).toBe(401)
    expect(((await run(reply(403, {}))) as ApiError).status).toBe(403)
    expect(((await run(reply(502, {}))) as ApiError).message).toBe(EMPLOYEE_SERVER_DOWN)
    const bad = (await run(reply(400, { title: 'Accept the declaration' }))) as ApiError
    expect([bad.status, bad.message]).toEqual([400, 'Accept the declaration'])
  })

  it('is 401 without an access token', async () => {
    const err = await createHttpEmployeeApi('', async () => null, vi.fn()).listInternalJobs('idp:1').catch((e: ApiError) => e)
    expect((err as ApiError).status).toBe(401)
  })
})
