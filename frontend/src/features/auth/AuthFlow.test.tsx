import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { App } from '../../App'
import { DEMO_PASSWORD } from '../../api/mock/seed'
import { renderWithProviders } from '../../test/render'

// Keycloak, as the portal sees it: the redirect is recorded instead of leaving the page.
const keycloak = vi.hoisted(() => ({
  startSignIn: vi.fn(() => Promise.resolve()),
  finishSignIn: vi.fn(),
  currentAccount: vi.fn(() => Promise.resolve(null)),
  forget: vi.fn(() => Promise.resolve()),
  signOut: vi.fn(() => Promise.resolve()),
}))
vi.mock('../../api/http/keycloak', () => ({ createKeycloakIdentityProvider: () => keycloak }))

const NORTHWIND = {
  id: 'a3a0f6a4-3f43-4a8b-9a54-1b0c7c4c0001',
  slug: 'northwind-talent',
  name: 'Northwind Talent',
  orgType: 'agency',
  careersTagline: 'Find your next role through us',
  emailDomain: 'northwind.example',
  locale: 'en-IN',
  currency: 'INR',
  ssoProviders: ['google', 'microsoft'],
  mfaRoles: ['hrhead', 'mdceo'],
  sessionIdleMinutes: 30,
}

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

/** The admin API: knows Northwind Talent and answers sign-ups like the real one. */
function stubAdminApi() {
  const fetcher = vi.fn((url: string, init?: RequestInit) => {
    if (init?.method === 'POST') {
      return Promise.resolve(json({ workspace: NORTHWIND, owner: { id: 'kc-1', name: 'J. Doe', email: 'j.doe@northwind.example', role: 'hrhead' } }, 201))
    }
    if (url.endsWith('/api/v1/me/access')) {
      const hrta = { key: 'hrta', name: 'HR-TA', permissions: ['mrf.raise', 'candidate.log'], pages: ['dashboard'], requiresMfa: false }
      return Promise.resolve(json({ workspaceId: NORTHWIND.id, roles: [hrta], permissions: hrta.permissions, pages: hrta.pages }))
    }
    return Promise.resolve(url.endsWith('/northwind-talent') ? json(NORTHWIND) : json({ title: 'Not found' }, 404))
  })
  vi.stubGlobal('fetch', fetcher)
  return fetcher
}

describe('sign-in and sign-out (RCU-PLT-001)', () => {
  afterEach(() => vi.unstubAllGlobals())


  it('sends signed-out visitors to a tenant-branded sign-in and lands HR-TA on the dashboard', async () => {
    const user = userEvent.setup()
    renderWithProviders(<App />, { signedIn: false, route: '/?' })
    await user.click(await screen.findByRole('button', { name: 'Open the demo workspace' }))

    expect(await screen.findByRole('heading', { name: 'Sign in to Aurora Housing Finance' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Continue with company SSO (SAML)' })).toBeInTheDocument()

    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument()
    await user.type(screen.getByLabelText('Email'), 'a.sharma@aurora-demo.example')
    await user.click(screen.getByRole('button', { name: 'Continue' }))
    expect(await screen.findByRole('heading', { name: 'Enter your password' })).toBeInTheDocument()
    expect(screen.getByText('a.sharma@aurora-demo.example')).toBeInTheDocument()
    await user.type(screen.getByLabelText('Password'), DEMO_PASSWORD)
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('navigation', { name: 'Main navigation' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Dashboard' })).toHaveClass('active')
  })

  it('explains a failed sign-in without saying which part was wrong', async () => {
    const user = userEvent.setup()
    renderWithProviders(<App />, { signedIn: false, route: '/signin?workspace=aurora' })
    await user.type(await screen.findByLabelText('Email'), 'a.sharma@aurora-demo.example')
    await user.click(screen.getByRole('button', { name: 'Continue' }))
    await user.type(await screen.findByLabelText('Password'), 'wrong')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('That email and password don’t match')
    expect(screen.getByLabelText('Password')).toHaveValue('')
    expect(screen.getByLabelText('Password')).toHaveFocus()
  })

  it('checks the email before asking for the password, and goes back with the email kept', async () => {
    const user = userEvent.setup()
    renderWithProviders(<App />, { signedIn: false, route: '/signin?workspace=aurora' })
    await user.type(await screen.findByLabelText('Email'), 'not-an-email')
    await user.click(screen.getByRole('button', { name: 'Continue' }))
    expect(screen.getByText('Enter a valid email address.')).toBeInTheDocument()

    await user.clear(screen.getByLabelText('Email'))
    await user.type(screen.getByLabelText('Email'), 'a.sharma@aurora-demo.example')
    await user.click(screen.getByRole('button', { name: 'Continue' }))
    await user.click(await screen.findByRole('button', { name: 'Sign in' }))
    expect(screen.getByText('Enter your password.')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Use a different email' }))
    expect(await screen.findByLabelText('Email')).toHaveValue('a.sharma@aurora-demo.example')
    await user.click(screen.getByRole('button', { name: 'Back' }))
    expect(await screen.findByLabelText('Workspace')).toBeInTheDocument()
  })

  it('opens on the email step with the address filled in when coming back from the password page', async () => {
    renderWithProviders(<App />, { signedIn: false, route: '/signin?workspace=aurora&email=a.sharma%40aurora-demo.example' })
    expect(await screen.findByLabelText('Email')).toHaveValue('a.sharma@aurora-demo.example')
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

  it('creates a workspace for a recruitment agency and sends the owner to sign in to it', async () => {
    stubAdminApi()
    const user = userEvent.setup()
    renderWithProviders(<App />, { signedIn: false, route: '/signup' })
    await user.click(await screen.findByRole('radio', { name: /Recruitment agency/ }))
    await user.type(screen.getByLabelText('Organisation name'), 'Northwind Talent')
    expect(screen.getByText('The team signs in at /signin?workspace=northwind-talent')).toBeInTheDocument()
    await user.type(screen.getByLabelText('Full name'), 'J. Doe')
    expect(screen.getByLabelText('Role')).toHaveValue('hrhead')
    await user.type(screen.getByLabelText('Work email'), 'j.doe@northwind.example')
    await user.type(screen.getByLabelText('Password'), 'weak')
    await user.click(screen.getByRole('button', { name: 'Create workspace' }))
    expect(screen.getByText('Choose a password that meets every rule below.')).toBeInTheDocument()

    await user.clear(screen.getByLabelText('Password'))
    await user.type(screen.getByLabelText('Password'), 'Str0ng!pass')
    await user.click(screen.getByRole('checkbox'))
    await user.click(screen.getByRole('button', { name: 'Create workspace' }))
    await vi.waitFor(() =>
      expect(keycloak.startSignIn).toHaveBeenCalledWith({
        workspace: 'northwind-talent',
        email: 'j.doe@northwind.example',
        brand: { name: 'Northwind Talent', tagline: 'Find your next role through us' },
      }),
    )
    expect(screen.getByRole('button', { name: 'Creating workspace…' })).toBeDisabled()
  })

  it('says so clearly when sign-up can’t reach the server', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('fetch failed')))
    const user = userEvent.setup()
    renderWithProviders(<App />, { signedIn: false, route: '/signup' })
    await user.type(await screen.findByLabelText('Organisation name'), 'Northwind Talent')
    await user.type(screen.getByLabelText('Full name'), 'J. Doe')
    await user.type(screen.getByLabelText('Work email'), 'j.doe@northwind.example')
    await user.type(screen.getByLabelText('Password'), 'Str0ng!pass')
    await user.click(screen.getByRole('checkbox'))
    await user.click(screen.getByRole('button', { name: 'Create workspace' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('We couldn’t reach the Recuro server to create your workspace.')
    expect(screen.getByRole('button', { name: 'Create workspace' })).toBeEnabled()
  })
})

describe('sign-in for a workspace saved on the server', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('asks for the email, then hands over to Keycloak for the password', async () => {
    stubAdminApi()
    const user = userEvent.setup()
    renderWithProviders(<App />, { signedIn: false, route: '/signin?workspace=northwind-talent' })
    expect(await screen.findByRole('heading', { name: 'Sign in to Northwind Talent' })).toBeInTheDocument()
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument()
    await user.type(screen.getByLabelText('Email'), 'j.doe@northwind.example')
    await user.click(screen.getByRole('button', { name: 'Continue' }))
    await vi.waitFor(() =>
      expect(keycloak.startSignIn).toHaveBeenCalledWith({
        workspace: 'northwind-talent',
        email: 'j.doe@northwind.example',
        returnTo: undefined,
        brand: { name: 'Northwind Talent', tagline: 'Find your next role through us' },
      }),
    )
    expect(screen.getByRole('button', { name: 'One moment…' })).toBeDisabled()
  })

  it('opens the workspace when Keycloak sends the person back', async () => {
    stubAdminApi()
    keycloak.finishSignIn.mockResolvedValueOnce({
      subject: 'kc-1',
      name: 'J. Doe',
      email: 'j.doe@northwind.example',
      tenantId: NORTHWIND.id,
      roles: ['tenant-admin', 'hrta'],
      workspaceRoles: ['hrta'],
      accessToken: 'token',
      workspace: 'northwind-talent',
      returnTo: null,
    })
    renderWithProviders(<App />, { signedIn: false, route: '/auth/callback?code=abc&state=xyz' })
    expect(await screen.findByRole('navigation', { name: 'Main navigation' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Account menu, signed in as J\. Doe/ })).toBeInTheDocument()
  })

  it('explains a sign-in Keycloak refused for this workspace', async () => {
    stubAdminApi()
    keycloak.finishSignIn.mockResolvedValueOnce({
      subject: 'kc-2',
      name: 'Someone Else',
      email: 'x@other.example',
      tenantId: 'another-tenant',
      roles: ['hrta'],
      workspaceRoles: [],
      accessToken: 'token',
      workspace: 'northwind-talent',
      returnTo: null,
    })
    renderWithProviders(<App />, { signedIn: false, route: '/auth/callback?code=def&state=uvw' })
    expect(await screen.findByRole('alert')).toHaveTextContent('This account belongs to a different workspace.')
    expect(screen.getByRole('link', { name: 'Back to sign in' })).toBeInTheDocument()
  })
})

describe('role landing and second factor', () => {
  it('asks the HR Head for an emailed code, then opens their workspace', async () => {
    const user = userEvent.setup()
    renderWithProviders(<App />, { signedIn: false, route: '/signin?workspace=aurora' })
    await user.type(await screen.findByLabelText('Email'), 'k.iyer@aurora-demo.example')
    await user.click(screen.getByRole('button', { name: 'Continue' }))
    await user.type(await screen.findByLabelText('Password'), DEMO_PASSWORD)
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
