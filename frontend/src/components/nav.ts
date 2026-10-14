import type { Role } from '../domain/types'

export interface NavItem {
  to: string
  key: string
  /** Screen key in the admin screen catalogue; a workspace role's configured pages are listed by it. */
  screen: string
  /** Who sees the item when the workspace hasn't configured the signed-in role. */
  roles: Role[]
}

const INTERNAL: Role[] = ['hrta', 'hrhead', 'mdceo']

export const NAV_ITEMS: NavItem[] = [
  { to: '/', key: 'dashboard', screen: 'dashboard', roles: INTERNAL },
  { to: '/mrf/new', key: 'newMrf', screen: 'mrf', roles: INTERNAL },
  { to: '/jd', key: 'jd', screen: 'jd', roles: INTERNAL },
  { to: '/approvals', key: 'approvals', screen: 'approvals', roles: INTERNAL },
  { to: '/pipeline', key: 'pipeline', screen: 'pipeline', roles: INTERNAL },
  { to: '/assessment', key: 'assessment', screen: 'assessment', roles: INTERNAL },
  { to: '/offer', key: 'offer', screen: 'offer', roles: INTERNAL },
  { to: '/bgv', key: 'bgv', screen: 'bgv', roles: INTERNAL },
  { to: '/onboarding', key: 'onboarding', screen: 'onboarding', roles: INTERNAL },
  { to: '/vendors', key: 'vendors', screen: 'vendors', roles: INTERNAL },
  { to: '/reports', key: 'reports', screen: 'reports', roles: INTERNAL },
  { to: '/internal-careers', key: 'internalCareers', screen: 'internalCareers', roles: ['employee'] },
  { to: '/careers', key: 'careers', screen: 'careers', roles: ['employee', 'candidate'] },
]
