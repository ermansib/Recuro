import { captureSignInLook, decodeSignInBrand, encodeSignInBrand, type SignInBrand } from './signInBrand'

describe('workspace look carried to the password page', () => {
  it('round-trips the workspace name, tagline and colours, accents included', () => {
    const brand: SignInBrand = {
      v: 1,
      slug: 'mb-consulting',
      name: 'MB Consulting · Café',
      tagline: 'Hiring, made calm',
      scheme: 'dark',
      tokens: { navy: '#1E2A5E', card: 'rgb(21, 28, 54)' },
    }
    const encoded = encodeSignInBrand(brand)
    expect(encoded).toMatch(/^[A-Za-z0-9_-]+$/)
    expect(decodeSignInBrand(encoded)).toEqual(brand)
  })

  it('drops anything that isn’t a plain colour, so the page can’t be fed CSS', () => {
    const tampered = encodeSignInBrand({
      v: 1,
      slug: 'x',
      name: 'X',
      tagline: '',
      scheme: 'light',
      tokens: { navy: 'red;background:url(https://evil.example)', gold: '#7C3AED' },
    })
    expect(decodeSignInBrand(tampered)?.tokens).toEqual({ gold: '#7C3AED' })
    expect(decodeSignInBrand('not base64 json')).toBeNull()
  })

  it('reads the colours and mode the portal is showing', () => {
    const root = document.createElement('div')
    root.dataset.theme = 'dark'
    root.style.setProperty('--navy', '#A5B4FC')
    root.style.setProperty('--bg', 'var(--something)')
    expect(captureSignInLook(root)).toEqual({ scheme: 'dark', tokens: { navy: '#A5B4FC' } })
  })
})
