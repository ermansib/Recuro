import { unmetPasswordRules } from '@workspace-form'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import type { CreateMemberRequest, CreateRoleRequest, ResetMemberPasswordRequest, RoleCatalog, UpdateMemberRequest, WorkspaceMember, WorkspaceRole } from '../../api/types'
import { fakeApi, renderWithApi } from '../../test/render'
import { suggestTemporaryPassword } from '../../utils/password'
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

  it('adds a person with their roles and a temporary password', async () => {
    const createMember = vi.fn((_scope: string | undefined, request: CreateMemberRequest) =>
      Promise.resolve({ accountId: 'kc-2', name: request.name, email: request.email, roleKeys: request.roleKeys, department: request.department }),
    )
    renderPage({ createMember })

    await userEvent.click(await screen.findByRole('button', { name: 'Add a person' }))
    await userEvent.click(screen.getByRole('button', { name: 'Add person' }))
    expect(screen.getByText('Give the person at least one role.')).toBeInTheDocument()
    expect(createMember).not.toHaveBeenCalled()

    await userEvent.type(screen.getByLabelText('Full name'), 'Ravi Kumar')
    await userEvent.type(screen.getByLabelText('Email'), 'ravi@aurora.example')
    await userEvent.click(screen.getByRole('checkbox', { name: 'HR-TA' }))
    await userEvent.type(screen.getByLabelText('Temporary password'), 'Temp!pass1')
    await userEvent.click(screen.getByRole('button', { name: 'Add person' }))

    await waitFor(() =>
      expect(createMember).toHaveBeenCalledWith(undefined, {
        name: 'Ravi Kumar',
        email: 'ravi@aurora.example',
        roleKeys: ['hrta'],
        department: null,
        temporaryPassword: 'Temp!pass1',
      }),
    )
    expect(await screen.findByText(/Ravi Kumar can now sign in/)).toBeInTheDocument()
  })

  it('sets a temporary password for someone who forgot theirs', async () => {
    const resetMemberPassword = vi.fn((_scope: string | undefined, _accountId: string, _request: ResetMemberPasswordRequest) => Promise.resolve())
    renderPage({ resetMemberPassword })

    await userEvent.click(await screen.findByRole('button', { name: 'Reset password for Asha Rao' }))
    await userEvent.type(screen.getByLabelText('Temporary password'), 'weak')
    await userEvent.click(screen.getByRole('button', { name: 'Set temporary password' }))
    expect(resetMemberPassword).not.toHaveBeenCalled()

    await userEvent.click(screen.getByRole('button', { name: 'Suggest a password' }))
    const suggested = (screen.getByLabelText('Temporary password') as HTMLInputElement).value
    await userEvent.click(screen.getByRole('button', { name: 'Set temporary password' }))

    await waitFor(() => expect(resetMemberPassword).toHaveBeenCalledWith(undefined, 'kc-1', { temporaryPassword: suggested }))
    expect(await screen.findByText((text) => text.includes(`Temporary password for Asha Rao is ${suggested}.`))).toBeInTheDocument()
  })
})

describe('suggestTemporaryPassword', () => {
  it('always meets every password rule', () => {
    for (let i = 0; i < 200; i++) expect(unmetPasswordRules(suggestTemporaryPassword())).toEqual([])
  })
})
