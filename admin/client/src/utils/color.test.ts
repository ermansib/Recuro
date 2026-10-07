import { contrastRatio, readableTextOn } from './color'

describe('color', () => {
  it('matches the WCAG reference ratios used by the API', () => {
    expect(contrastRatio('#000000', '#FFFFFF')).toBeCloseTo(21, 1)
    expect(contrastRatio('#777777', '#FFFFFF')).toBeCloseTo(4.48, 2)
  })

  it('picks readable text for light and dark backgrounds', () => {
    expect(readableTextOn('#1E2A5E')).toBe('#FFFFFF')
    expect(readableTextOn('#22D3EE')).toBe('#111827')
  })
})
