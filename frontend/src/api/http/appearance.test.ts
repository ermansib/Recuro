import { describe, expect, it, vi } from 'vitest'
import { createHttpAppearanceApi, toTenantAppearance } from './appearance'

const dto = {
  tenantName: 'Aurora Housing Finance',
  slug: 'aurora',
  kind: 'enterprise',
  themePresetKey: 'emerald-trust',
  themeMode: 'dark',
  lightTheme: { navy: '#065F46', cy: '#34D399', gold: '#D97706', bg: '#F3FAF7', card: '#FFFFFF', txt: '#0F172A' },
  darkTheme: { navy: '#6EE7B7', cy: '#34D399', gold: '#FBBF24', bg: '#07140F', card: '#0F241C', txt: '#E5E7EB' },
  screens: [],
}

describe('admin portal runtime config → TenantAppearance', () => {
  it('maps the RuntimeConfigDto', () => {
    expect(toTenantAppearance(dto)).toEqual({
      slug: 'aurora',
      themePresetKey: 'emerald-trust',
      themeMode: 'dark',
      lightTheme: dto.lightTheme,
      darkTheme: dto.darkTheme,
    })
  })

  it('drops anything that is not a hex colour, so only colours reach CSS', () => {
    const result = toTenantAppearance({ ...dto, lightTheme: { ...dto.lightTheme, navy: 'red;background:url(x)', 'Bad Key': '#000000' } })
    expect(result?.lightTheme.navy).toBeUndefined()
    expect(result?.lightTheme['Bad Key']).toBeUndefined()
    expect(result?.lightTheme.gold).toBe('#D97706')
  })

  it('falls back to following the device for an unknown mode', () => {
    expect(toTenantAppearance({ ...dto, themeMode: 'sepia' })?.themeMode).toBe('system')
  })

  it('rejects bodies that are not a runtime config', () => {
    expect(toTenantAppearance(null)).toBeNull()
    expect(toTenantAppearance({ slug: 'aurora' })).toBeNull()
  })

  it('calls the runtime endpoint for the slug', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify(dto), { status: 200 }))
    const result = await createHttpAppearanceApi('https://admin.example', fetcher).getTenantAppearance('aurora')
    expect(fetcher).toHaveBeenCalledWith('https://admin.example/api/v1/runtime/aurora', expect.anything())
    expect(result?.themePresetKey).toBe('emerald-trust')
  })

  it('resolves null when the admin portal is down or does not know the tenant', async () => {
    const down = createHttpAppearanceApi('', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))
    const unknown = createHttpAppearanceApi('', vi.fn().mockResolvedValue(new Response('', { status: 404 })))
    await expect(down.getTenantAppearance('aurora')).resolves.toBeNull()
    await expect(unknown.getTenantAppearance('nobody')).resolves.toBeNull()
  })
})
