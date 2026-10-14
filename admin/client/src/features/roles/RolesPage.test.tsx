import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import type { CreateRoleRequest, RoleCatalog, UpdateMemberRequest, WorkspaceMember, WorkspaceRole } from '../../api/types'
import { fakeApi, renderWithApi } from '../../test/render'
import { RolesPage } from './RolesPage'

const CATALOG: RoleCatalog = {
  permissions: [
    { key: 'candidate.log', module: 'Candidates', label: 'Add candidates' },
    { key: 'pipeline.move', module: 'Candidates', label: 'Move candidates between stages' },
    { key: 'offer.approve', module: 'Offers', label: 'Approve offers' },
  ],
  pages: [
    { key: 'dashboard', code: 'S-01', title: 'Dashboard', module: 'Dashboard' },
    { key: 'pipeline', code: 'S-06', title: 'Candidate pipeline', module: 'Candidates' },
  ],
}

const role = (key: string, name: string, patch: Partial<WorkspaceRole> = {}): WorkspaceRole => ({
  key,
  name,
  description: null,
  reportsTo: null,
  permissions: [],
  pages: ['dashboard'],
  requiresMfa: false,
  isSystem: true,
  isActive: true,
  memberCount: 0,
  ...patch,
})

const ROLES = [role('hrhead', 'HR Head', { requiresMfa: true, memberCount: 1 }), role('hrta', 'HR-TA', { reportsTo: 'hrhead' })]
const MEMBERS: WorkspaceMember[] = [{ accountId: 'kc-1', name: 'Asha Rao', email: 'asha@aurora.example', roleKeys: ['hrhead'], department: null }]

function renderPage(overrides: Parameters<typeof fakeApi>[0] = {}) {
  const api = fakeApi({
    getRoleCatalog: () => Promise.resolve(CATALOG),
    listRoles: () => Promise.resolve(ROLES),
    listMembers: () => Promise.resolve(MEMBERS),
    ...overrides,
  })
  renderWithApi(<RolesPage />, { api })
  return api
}

describe('RolesPage', () => {
  it('lists the workspace roles with their reporting line and people', async () => {
    renderPage()

    const row = (await screen.findByRole('rowheader', { name: /HR-TA/ })).closest('tr')!
    expect(within(row).getByText('HR Head')).toBeInTheDocument()
    expect(screen.getByRole('rowheader', { name: 'Asha Rao' })).toBeInTheDocument()
  })

  it('creates a role with a key suggested from its name', async () => {
    const createRole = vi.fn((_scope: string | undefined, request: CreateRoleRequest) =>
      Promise.resolve(role(request.key, request.name, { isSystem: false })),
    )
    renderPage({ createRole })

    await userEvent.click(await screen.findByRole('button', { name: 'Add a role' }))
    await userEvent.type(screen.getByLabelText('Role name'), 'Area Manager')
    await userEvent.selectOptions(screen.getByLabelText('Reports to'), 'hrhead')
    await userEvent.click(screen.getByRole('checkbox', { name: 'Move candidates between stages' }))
    await userEvent.click(screen.getByRole('checkbox', { name: 'S-06 · Candidate pipeline' }))
    await userEvent.click(screen.getByRole('button', { name: 'Create role' }))

    await waitFor(() => expect(createRole).toHaveBeenCalled())
    const [scope, request] = createRole.mock.calls[0]!
    expect(scope).toBeUndefined()
    expect(request).toEqual({
      key: 'area-manager',
      name: 'Area Manager',
      description: null,
      reportsTo: 'hrhead',
      permissions: ['pipeline.move'],
      pages: ['pipeline'],
      requiresMfa: false,
    })
  })

  it('gives a person another role', async () => {
    const updateMember = vi.fn((_scope: string | undefined, _accountId: string, request: UpdateMemberRequest) =>
      Promise.resolve({ ...MEMBERS[0]!, ...request }),
    )
    renderPage({ updateMember })

    await userEvent.click(await screen.findByRole('button', { name: 'Edit roles for Asha Rao' }))
    await userEvent.click(screen.getByRole('checkbox', { name: 'HR-TA' }))
    await userEvent.type(screen.getByRole('textbox', { name: 'Department for Asha Rao' }), 'Talent')
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))

    await waitFor(() => expect(updateMember).toHaveBeenCalledWith(undefined, 'kc-1', { roleKeys: ['hrhead', 'hrta'], department: 'Talent' }))
  })
})
