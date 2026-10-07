import { maskEmail, slugify, unmetPasswordRules } from './auth'

describe('identity rules (RCU-PLT-001)', () => {
  it('lists every password rule a weak password misses', () => {
    expect(unmetPasswordRules('abc')).toEqual(['length', 'upper', 'digit', 'symbol'])
    expect(unmetPasswordRules('Recuro@2026')).toEqual([])
  })

  it('turns organisation names into workspace slugs', () => {
    expect(slugify('  Aurora Housing Finance Pvt. Ltd. ')).toBe('aurora-housing-finance-pvt-ltd')
    expect(slugify('Ünïcode & Co')).toBe('unicode-co')
  })

  it('masks an email without hiding the domain', () => {
    expect(maskEmail('k.iyer@aurora.example')).toBe('k.***@aurora.example')
    expect(maskEmail('a@x.io')).toBe('a***@x.io')
  })
})
