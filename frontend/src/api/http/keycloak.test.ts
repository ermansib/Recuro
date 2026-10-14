import { describe, expect, it } from 'vitest'
import { accountOf, readJwtClaims } from './keycloak'

const b64url = (value: unknown) => btoa(JSON.stringify(value)).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, '')
const jwt = (claims: unknown) => `${b64url({ alg: 'RS256' })}.${b64url(claims)}.signature`

describe('Keycloak tokens', () => {
  it('reads the workspace id and roles from the access token and the name from the ID token', () => {
    const account = accountOf(
      {
        access_token: jwt({ sub: 'kc-1', tenant_id: 'tnt-1', roles: ['tenant-admin', 'hrhead', 7] }),
        profile: { sub: 'kc-1', name: 'Mansi Bansal', email: 'owner@mb.example' } as never,
        state: { workspace: 'mb-consulting', returnTo: '/team' },
      },
      null,
    )
    expect(account).toEqual({
      subject: 'kc-1',
      name: 'Mansi Bansal',
      email: 'owner@mb.example',
      tenantId: 'tnt-1',
      roles: ['tenant-admin', 'hrhead'],
      workspace: 'mb-consulting',
      returnTo: '/team',
    })
  })

  it('falls back to the remembered workspace when the stored user has no sign-in state', () => {
    const account = accountOf({ access_token: jwt({ sub: 'kc-1' }), profile: { sub: 'kc-1' } as never, state: undefined }, 'mb-consulting')
    expect(account).toMatchObject({ workspace: 'mb-consulting', tenantId: null, roles: [] })
  })

  it('treats anything that is not a JWT as having no claims', () => {
    expect(readJwtClaims('opaque')).toEqual({})
    expect(readJwtClaims('a.%%%.c')).toEqual({})
  })
})
