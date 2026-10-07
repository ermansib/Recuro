const DARK_TEXT = '#111827'
const LIGHT_TEXT = '#FFFFFF'

function luminance(hex: string): number {
  const channel = (start: number) => {
    const srgb = parseInt(hex.slice(start, start + 2), 16) / 255
    return srgb <= 0.03928 ? srgb / 12.92 : ((srgb + 0.055) / 1.055) ** 2.4
  }
  return 0.2126 * channel(1) + 0.7152 * channel(3) + 0.0722 * channel(5)
}

/** WCAG contrast ratio between two #RRGGBB colours (same formula as the API's publish check). */
export function contrastRatio(a: string, b: string): number {
  const [lighter, darker] = [luminance(a), luminance(b)].sort((x, y) => y - x) as [number, number]
  return (lighter + 0.05) / (darker + 0.05)
}

/** Picks dark or white text, whichever reads better on the given background. */
export function readableTextOn(background: string): string {
  return contrastRatio(background, DARK_TEXT) >= contrastRatio(background, LIGHT_TEXT) ? DARK_TEXT : LIGHT_TEXT
}
