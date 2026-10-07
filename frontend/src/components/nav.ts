import type { Role } from '../domain/types'

export interface NavItem {
  to: string
  key: string
  roles: Role[]
}

const INTERNAL: Role[] = ['hrta', 'hrhead', 'mdceo']

export const NAV_ITEMS: NavItem[] = [
  { to: '/', key: 'dashboard', roles: INTERNAL },
  { to: '/mrf/new', key: 'newMrf', roles: INTERNAL },
  { to: '/jd', key: 'jd', roles: INTERNAL },
  { to: '/approvals', key: 'approvals', roles: INTERNAL },
  { to: '/pipeline', key: 'pipeline', roles: INTERNAL },
  { to: '/assessment', key: 'assessment', roles: INTERNAL },
  { to: '/offer', key: 'offer', roles: INTERNAL },
  { to: '/bgv', key: 'bgv', roles: INTERNAL },
  { to: '/onboarding', key: 'onboarding', roles: INTERNAL },
  { to: '/vendors', key: 'vendors', roles: INTERNAL },
  { to: '/reports', key: 'reports', roles: INTERNAL },
  { to: '/internal-careers', key: 'internalCareers', roles: ['employee'] },
  { to: '/careers', key: 'careers', roles: ['employee', 'candidate'] },
]
