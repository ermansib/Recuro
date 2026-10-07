import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { App } from '../../App'
import { DEMO_PASSWORD } from '../../api/mock/seed'
import { renderWithProviders } from '../../test/render'

describe('sign-in and sign-out (RCU-PLT-001)', () => {
  it('sends signed-out visitors to a tenant-branded sign-in and lands HR-TA on the dashboard', async () => {
    const user = userEvent.setup()
    renderWithProviders(<App />, { signedIn: false, route: '/?' })
    await user.click(await screen.findByRole('button', { name: 'Open the demo workspace' }))

    expect(await screen.findByRole('heading', { name: 'Sign in to Aurora Housing Finance' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Continue with company SSO (SAML)' })).toBeInTheDocument()

    await user.type(screen.getByLabelText('Email'), 'a.sharma@aurora-demo.example')
    await user.type(screen.getByLabelText('Password'), DEMO_PASSWORD)
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('navigation', { name: 'Main navigation' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Dashboard' })).toHaveClass('active')
  })

  it('explains a failed sign-in without saying which part was wrong', async () => {
    const user = userEvent.setup()
    renderWithProviders(<App />, { signedIn: false, route: '/signin?workspace=aurora' })
    await user.type(await screen.findByLabelText('Email'), 'a.sharma@aurora-demo.example')
    await user.type(screen.getByLabelText('Password'), 'wrong')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('That email and password don’t match')
  })

  it('signs out from the account menu and returns to sign-in', async () => {
    const user = userEvent.setup()
    renderWithProviders(<App />, { role: 'hrta', route: '/' })
    await user.click(await screen.findByRole('button', { name: /Account menu, signed in as A\. Sharma/ }))
    await user.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Sign out' }))
    expect(await screen.findByRole('heading', { name: 'Sign in to Aurora Housing Finance' })).toBeInTheDocument()
    expect(screen.getByText('You’ve signed out.', { selector: '.auth-info' })).toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Main navigation' })).not.toBeInTheDocument()
  })

  it('creates a workspace for a recruitment agency and lands the owner in it', async () => {
    const user = userEvent.setup()
    renderWithProviders(<App />, { signedIn: false, route: '/signup' })
    await user.click(await screen.findByRole('radio', { name: /Recruitment agency/ }))
    await user.type(screen.getByLabelText('Organisation name'), 'Northwind Talent')
    expect(screen.getByText('Your team will sign in at /signin?workspace=northwind-talent')).toBeInTheDocument()
    await user.type(screen.getByLabelText('Full name'), 'J. Doe')
    expect(screen.getByLabelText('Your role')).toHaveValue('hrhead')
    await user.type(screen.getByLabelText('Work email'), 'j.doe@northwind.example')
    await user.type(screen.getByLabelText('Password'), 'weak')
    await user.click(screen.getByRole('button', { name: 'Create workspace' }))
    expect(screen.getByText('Choose a password that meets every rule below.')).toBeInTheDocument()

    await user.clear(screen.getByLabelText('Password'))
    await user.type(screen.getByLabelText('Password'), 'Str0ng!pass')
    await user.click(screen.getByRole('checkbox'))
    await user.click(screen.getByRole('button', { name: 'Create workspace' }))
    expect(await screen.findByText(/Northwind Talent · White-label recruitment CRM/)).toBeInTheDocument()
  })
})

describe('role landing and second factor', () => {
  it('asks the HR Head for an emailed code, then opens their workspace', async () => {
    const user = userEvent.setup()
    renderWithProviders(<App />, { signedIn: false, route: '/signin?workspace=aurora' })
    await user.type(await screen.findByLabelText('Email'), 'k.iyer@aurora-demo.example')
    await user.type(screen.getByLabelText('Password'), DEMO_PASSWORD)
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByRole('heading', { name: 'Check your email' })).toBeInTheDocument()
    const code = screen.getByText(/^Demo code: \d{6}$/).textContent?.slice(-6) ?? ''
    await user.type(screen.getByLabelText('Verification code'), code)
    await user.click(screen.getByRole('button', { name: 'Verify and sign in' }))
    expect(await screen.findByRole('navigation', { name: 'Main navigation' })).toBeInTheDocument()
  })

  it('lands a new candidate on the careers site', async () => {
    const user = userEvent.setup()
    renderWithProviders(<App />, { signedIn: false, route: '/signup/candidate?workspace=aurora' })
    await user.type(await screen.findByLabelText('Full name'), 'Priya N')
    await user.type(screen.getByLabelText('Email'), 'priya.ui@mail.example')
    await user.type(screen.getByLabelText('Password'), 'Str0ng!pass')
    await user.click(screen.getByRole('checkbox'))
    await user.click(screen.getByRole('button', { name: 'Create account' }))
    expect(await screen.findByRole('link', { name: 'Careers' })).toHaveClass('active')
  })
})
