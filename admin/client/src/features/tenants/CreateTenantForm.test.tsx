import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import type { CreateTenantRequest, CurrentAdmin, Tenant } from '../../api/types'
import { fakeApi, renderWithApi } from '../../test/render'
import { CreateTenantForm } from './CreateTenantForm'

const PLATFORM: CurrentAdmin = { name: 'Recuro Platform', level: 'platform', tenantId: null, tenantName: null }

describe('Add a workspace', () => {
  it('asks for the same workspace and owner details as the portal sign-up page, plus plan and domain', async () => {
    const createTenant = vi.fn((request: CreateTenantRequest): Promise<Tenant> =>
      Promise.resolve({
        id: 't-1',
        name: request.orgName,
        slug: 'northwind-talent',
        kind: 'agency',
        orgType: request.orgType,
        emailDomain: 'northwind.example',
        plan: request.plan,
        status: 'active',
        customDomain: request.customDomain,
        themePresetKey: 'recuro-classic',
        themeMode: 'system',
        createdAt: '2026-10-15T00:00:00Z',
      }),
    )
    const user = userEvent.setup()
    renderWithApi(<CreateTenantForm />, { api: fakeApi({ createTenant }), admin: PLATFORM })

    await user.click(screen.getByRole('button', { name: 'Add workspace' }))
    expect(await screen.findByText(/Enter the organisation’s name/)).toBeInTheDocument()
    expect(screen.getByText(/Enter the owner’s full name/)).toBeInTheDocument()
    expect(screen.getByText('Check the 4 highlighted fields below.')).toBeInTheDocument()
    expect(screen.getByLabelText('Organisation name')).toHaveFocus()
    expect(createTenant).not.toHaveBeenCalled()

    await user.click(screen.getByRole('radio', { name: /Recruitment agency/ }))
    expect(screen.getByLabelText('Role')).toHaveValue('hrhead')
    await user.type(screen.getByLabelText('Organisation name'), 'Northwind Talent')
    expect(screen.getByText('The team signs in at /signin?workspace=northwind-talent')).toBeInTheDocument()
    expect(screen.queryByText(/Enter the organisation’s name/)).not.toBeInTheDocument()
    expect(screen.getByText('Check the 3 highlighted fields below.')).toBeInTheDocument()
    await user.type(screen.getByLabelText('Full name'), 'J. Doe')
    await user.type(screen.getByLabelText('Work email'), 'j.doe@northwind.example')
    await user.type(screen.getByLabelText('Temporary password'), 'Recuro@2026')
    await user.selectOptions(screen.getByLabelText('Plan'), 'professional')
    await user.click(screen.getByRole('button', { name: 'Add workspace' }))

    await waitFor(() =>
      expect(createTenant).toHaveBeenCalledWith(
        {
          orgType: 'agency',
          orgName: 'Northwind Talent',
          adminName: 'J. Doe',
          adminRole: 'hrhead',
          email: 'j.doe@northwind.example',
          password: 'Recuro@2026',
          plan: 'professional',
          customDomain: null,
        },
      ),
    )
    expect(await screen.findByText(/Northwind Talent added/)).toBeInTheDocument()
  })

  it('suggests a temporary password that meets every rule and clears the form on request', async () => {
    const user = userEvent.setup()
    renderWithApi(<CreateTenantForm />, { api: fakeApi(), admin: PLATFORM })

    await user.click(screen.getByRole('button', { name: 'Suggest a password' }))
    const password = screen.getByLabelText('Temporary password') as HTMLInputElement
    expect(password.value).toHaveLength(14)
    expect(screen.getAllByText('(done)')).toHaveLength(5)

    await user.type(screen.getByLabelText('Organisation name'), 'Northwind Talent')
    await user.click(screen.getByRole('button', { name: 'Clear form' }))
    expect(screen.getByLabelText('Organisation name')).toHaveValue('')
    expect(password).toHaveValue('')
  })
})
