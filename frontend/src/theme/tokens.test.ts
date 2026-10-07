import { describe, expect, it } from 'vitest'
import type { ColorScheme, ThemeTokens } from '../domain/types'
import { contrastRatio } from '../utils/color'
import { deriveTokens, effectiveMode, resolveScheme, themeTokensFor } from './tokens'

// The six presets the admin portal ships (admin/server/.../Seed/ThemePresetSeed.cs), as the runtime
// endpoint returns them: [navy, cy, gold, bg, card, txt].
const PRESETS: Record<string, Record<ColorScheme, string[]>> = {
  'recuro-classic': { light: ['#1E2A5E', '#22D3EE', '#7C3AED', '#F4F6FB', '#FFFFFF', '#111827'], dark: ['#A5B4FC', '#22D3EE', '#A78BFA', '#0B1020', '#151C36', '#E5E7EB'] },
  'emerald-trust': { light: ['#065F46', '#34D399', '#D97706', '#F3FAF7', '#FFFFFF', '#0F172A'], dark: ['#6EE7B7', '#34D399', '#FBBF24', '#07140F', '#0F241C', '#E5E7EB'] },
  'royal-plum': { light: ['#4C1D95', '#C4B5FD', '#DB2777', '#F7F5FC', '#FFFFFF', '#111827'], dark: ['#C4B5FD', '#A78BFA', '#F472B6', '#120B1F', '#1E1433', '#EDE9FE'] },
  'ocean-teal': { light: ['#0F766E', '#5EEAD4', '#4F46E5', '#F2F9F9', '#FFFFFF', '#0F172A'], dark: ['#5EEAD4', '#2DD4BF', '#818CF8', '#061514', '#0E2422', '#E2E8F0'] },
  'sunset-coral': { light: ['#9A3412', '#FDBA74', '#0369A1', '#FDF7F3', '#FFFFFF', '#1C1917'], dark: ['#FDBA74', '#FB923C', '#7DD3FC', '#1A0D07', '#2A160C', '#F5F5F4'] },
  'graphite-gold': { light: ['#1F2937', '#9CA3AF', '#B45309', '#F5F5F4', '#FFFFFF', '#111827'], dark: ['#E5E7EB', '#9CA3AF', '#FBBF24', '#0C0C0D', '#1A1A1D', '#F3F4F6'] },
}

const palette = ([navy, cy, gold, bg, card, txt]: string[]): ThemeTokens => ({ navy, cy, gold, bg, card, txt }) as ThemeTokens

const cases = Object.entries(PRESETS).flatMap(([key, schemes]) =>
  (['light', 'dark'] as const).map((scheme) => ({ key, scheme, tokens: themeTokensFor(palette(schemes[scheme]), scheme) })),
)

describe('tenant theme tokens (RCU-PLT-006, NFR-06)', () => {
  it.each(cases)('$key $scheme keeps text readable (WCAG AA)', ({ tokens }) => {
    const t = (name: string) => tokens[name] ?? ''
    expect(contrastRatio(t('on-navy'), t('navy'))).toBeGreaterThanOrEqual(4.5)
    expect(contrastRatio(t('on-gold'), t('gold'))).toBeGreaterThanOrEqual(4.5)
    expect(contrastRatio(t('on-bar'), t('bar'))).toBeGreaterThanOrEqual(4.5)
    expect(contrastRatio(t('on-bar'), t('bar-dk'))).toBeGreaterThanOrEqual(4.5)
    expect(contrastRatio(t('on-bar-muted'), t('bar'))).toBeGreaterThanOrEqual(4.5)
    expect(contrastRatio(t('accent-tx'), t('gold-lt'))).toBeGreaterThanOrEqual(4.5)
    expect(contrastRatio(t('muted'), t('card'))).toBeGreaterThanOrEqual(4.5)
    expect(contrastRatio(t('muted'), t('bg'))).toBeGreaterThanOrEqual(4.5)
    expect(contrastRatio(t('txt-2'), t('card'))).toBeGreaterThanOrEqual(4.5)
    expect(contrastRatio(t('navy-lt'), t('card'))).toBeGreaterThanOrEqual(4.5)
    // Headings use the primary colour as text; the admin portal already holds it to 3:1.
    expect(contrastRatio(t('navy'), t('card'))).toBeGreaterThanOrEqual(3)
  })

  it('derives nothing from an empty theme, so styles/tokens.css stays in charge', () => {
    expect(themeTokensFor(undefined, 'light')).toEqual({})
    expect(themeTokensFor({}, 'dark')).toEqual({})
  })

  it('derives only what a partial theme supplies', () => {
    const derived = deriveTokens({ gold: '#B45309' }, 'light')
    expect(derived['gold-lt']).toBeDefined()
    expect(derived['on-navy']).toBeUndefined()
    expect(derived.line).toBeUndefined()
  })

  it('keeps bars dark in dark mode instead of using the light primary', () => {
    const tokens = themeTokensFor(palette(PRESETS['recuro-classic']!.dark), 'dark')
    expect(tokens.bar).toBe('#151C36')
    expect(tokens['bar-dk']).toBe('#0B1020')
  })
})

describe('colour mode', () => {
  it('prefers the user, then the tenant, then the device', () => {
    expect(effectiveMode('light', 'dark')).toBe('light')
    expect(effectiveMode(null, 'dark')).toBe('dark')
    expect(effectiveMode(null, undefined)).toBe('system')
  })

  it('resolves system against the device setting', () => {
    expect(resolveScheme('system', true)).toBe('dark')
    expect(resolveScheme('system', false)).toBe('light')
    expect(resolveScheme('dark', false)).toBe('dark')
  })
})
