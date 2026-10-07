import { vi } from 'vitest'
import { ApiError } from './contract'
import { createHttpClient, PERSONA_HEADER } from './httpClient'

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

describe('createHttpClient', () => {
  it('sends the credentials and the JSON body', async () => {
    const fetchImpl = vi.fn(() => Promise.resolve(jsonResponse(200, { tenantName: 'Aurora' })))
    const api = createHttpClient(() => Promise.resolve({ [PERSONA_HEADER]: 'platform' }), fetchImpl)

    await api.assignTenantTheme('t-1', { themePresetKey: 'royal-plum', themeMode: 'dark' })

    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe('/api/v1/platform/tenants/t-1/theme')
    expect(init.method).toBe('PUT')
    expect((init.headers as Record<string, string>)[PERSONA_HEADER]).toBe('platform')
    expect(JSON.parse(init.body as string)).toEqual({ themePresetKey: 'royal-plum', themeMode: 'dark' })
  })

  it('turns problem details into an ApiError with the domain code', async () => {
    const fetchImpl = vi.fn(() =>
      Promise.resolve(jsonResponse(400, { title: 'Field grade is locked.', code: 'screen.lockedField' })),
    )
    const api = createHttpClient(() => Promise.resolve({}), fetchImpl)

    const error = await api.getScreen('mrf').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ status: 400, code: 'screen.lockedField', message: 'Field grade is locked.' })
  })
})
