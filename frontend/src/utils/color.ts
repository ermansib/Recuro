// Colour maths for runtime theming: WCAG contrast and simple sRGB mixing of #rrggbb colours.

type Rgb = [number, number, number]

function toRgb(hex: string): Rgb {
  const n = Number.parseInt(hex.slice(1), 16)
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255]
}

function toHex([r, g, b]: Rgb): string {
  return `#${[r, g, b].map((c) => Math.round(c).toString(16).padStart(2, '0')).join('')}`.toUpperCase()
}

/** Mixes `weight` (0..1) of `other` into `hex`. */
export function mix(hex: string, other: string, weight: number): string {
  const a = toRgb(hex)
  const b = toRgb(other)
  return toHex([a[0] + (b[0] - a[0]) * weight, a[1] + (b[1] - a[1]) * weight, a[2] + (b[2] - a[2]) * weight])
}

function luminance(hex: string): number {
  const [r, g, b] = toRgb(hex).map((c) => {
    const s = c / 255
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4
  }) as Rgb
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

/** WCAG 2.1 contrast ratio, 1..21. */
export function contrastRatio(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x) as [number, number]
  return (hi + 0.05) / (lo + 0.05)
}

/** Whichever of the two candidates reads better on `background`. */
export function readableOn(background: string, light = '#FFFFFF', dark = '#111827'): string {
  return contrastRatio(background, light) >= contrastRatio(background, dark) ? light : dark
}
