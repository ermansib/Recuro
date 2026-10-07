// Turns the six brand colours a tenant picks in the admin portal (navy = primary, cy = secondary,
// gold = accent, bg, card, txt) into the full set of tokens the portal's CSS uses (RCU-PLT-006).
// Pure functions, so the same rules can move server-side later.
import type { ColorScheme, ThemeMode, ThemeTokens } from '../domain/types'
import { contrastRatio, mix, readableOn } from '../utils/color'

/** The built-in Recuro Classic brand colours; must match styles/tokens.css. */
const BUILT_IN: Record<ColorScheme, Required<Pick<ThemeTokens, 'navy' | 'gold' | 'bg' | 'card' | 'txt'>>> = {
  light: { navy: '#1E2A5E', gold: '#7C3AED', bg: '#F4F6FB', card: '#FFFFFF', txt: '#1E293B' },
  dark: { navy: '#A5B4FC', gold: '#A78BFA', bg: '#0B1020', card: '#151C36', txt: '#E5E7EB' },
}

const WHITE = '#FFFFFF'
const BLACK = '#000000'
const AA_TEXT = 4.5

/**
 * Softens `text` toward `toward` as far as `maxWeight` allows while it keeps AA contrast on every
 * background, so secondary text looks quieter without becoming hard to read.
 */
function softened(text: string, toward: string, maxWeight: number, backgrounds: string[]): string {
  for (let weight = maxWeight; weight > 0; weight -= 0.02) {
    const candidate = mix(text, toward, weight)
    if (backgrounds.every((bg) => contrastRatio(candidate, bg) >= AA_TEXT)) return candidate
  }
  return text
}

/**
 * Derives the dependent tokens (hover shades, tints, text on brand colours, bar colours) from the
 * brand tokens given. Returns only tokens whose source colour was supplied, so a partial theme leaves
 * the rest of styles/tokens.css in charge.
 */
export function deriveTokens(brand: ThemeTokens, scheme: ColorScheme): ThemeTokens {
  const base = { ...BUILT_IN[scheme], ...brand }
  const { navy, gold, bg, card, txt } = base
  const out: ThemeTokens = {}
  const dark = scheme === 'dark'

  if (brand.navy || brand.card) {
    out['on-navy'] = readableOn(navy, WHITE, dark ? bg : txt)
    out['navy-dk'] = dark ? mix(navy, bg, 0.35) : mix(navy, BLACK, 0.33)
    // Links and hover states: a lighter primary that still reads as text on the surface.
    out['navy-lt'] = softened(navy, WHITE, dark ? 0.3 : 0.18, [card])
    out['navy-tint'] = mix(navy, card, dark ? 0.82 : 0.9)
  }
  if (brand.gold || brand.card || brand.txt) {
    out['on-gold'] = readableOn(gold, WHITE, dark ? bg : txt)
    out['gold-lt'] = mix(gold, card, dark ? 0.82 : 0.9)
    out['accent-line'] = mix(gold, card, dark ? 0.6 : 0.75)
    out['accent-tx'] = softened(txt, gold, 0.55, [out['gold-lt']])
  }
  // Neutrals (panels, lines, tracks, secondary text) follow the tenant's surface and text colours,
  // so a green or plum theme doesn't sit on the default blue-grey greys.
  if (brand.card && brand.txt) {
    out['surface-2'] = mix(card, bg, 0.5)
    out['surface-3'] = dark ? mix(card, txt, 0.06) : mix(bg, navy, 0.04)
    out.hover = mix(card, navy, dark ? 0.08 : 0.04)
    out.line = mix(card, txt, dark ? 0.16 : 0.1)
    out['line-soft'] = mix(card, txt, dark ? 0.1 : 0.06)
    out.track = mix(card, txt, dark ? 0.14 : 0.08)
    out['track-2'] = mix(card, txt, dark ? 0.26 : 0.22)
    out['txt-2'] = softened(txt, card, 0.22, [card, bg])
    out.muted = softened(txt, card, 0.42, [card, bg])
    // Timestamps and footnotes: deliberately quieter than AA body text, never below 3:1.
    out.faint = mix(txt, card, dark ? 0.45 : 0.5)
  }
  // Bars stay dark in both modes: the tenant's primary in light mode, its surfaces in dark mode.
  if (dark ? brand.card || brand.bg : brand.navy) {
    const bar = dark ? card : navy
    out.bar = bar
    out['bar-dk'] = dark ? bg : mix(navy, BLACK, 0.33)
    out['bar-lt'] = dark ? mix(card, navy, 0.15) : mix(navy, WHITE, 0.18)
    out['on-bar'] = readableOn(bar, '#F8FAFC', '#111827')
    out['on-bar-muted'] = softened(out['on-bar'], bar, 0.25, [bar, out['bar-dk']])
  }
  return out
}

/** The tokens to set for a scheme: the tenant's brand colours plus everything derived from them. */
export function themeTokensFor(brand: ThemeTokens | undefined, scheme: ColorScheme): ThemeTokens {
  if (!brand || Object.keys(brand).length === 0) return {}
  return { ...brand, ...deriveTokens(brand, scheme) }
}

/** A user's own choice wins; otherwise the tenant's default; otherwise follow the device. */
export function effectiveMode(userChoice: ThemeMode | null, tenantDefault: ThemeMode | undefined): ThemeMode {
  return userChoice ?? tenantDefault ?? 'system'
}

export function resolveScheme(mode: ThemeMode, devicePrefersDark: boolean): ColorScheme {
  if (mode === 'system') return devicePrefersDark ? 'dark' : 'light'
  return mode
}
