import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, Navigate, useNavigate } from 'react-router-dom'
import { api } from '../../api/client'
import { useDemoAccess, useWorkspaceBranding } from '../../api/hooks'
import { homePath } from '../../auth/permissions'
import { useAuth } from '../../auth/sessionContext'
import { Loading } from '../../components/ui'
import { features } from '../../config/features'
import { tenant as demoTenant } from '../../config/tenant'
import { DEFAULT_IDLE_MINUTES, isValidEmail } from '../../domain/auth'
import type { Role, SignInResult, SsoProvider, WorkspaceBranding } from '../../domain/types'
import { AuthShell } from './AuthShell'
import { AuthField, FormError, PasswordField } from './fields'
import { useCompleteSignIn } from './useCompleteSignIn'
import { useWorkspaceParam, withWorkspace } from './useWorkspace'

type MfaChallenge = Extract<SignInResult, { status: 'mfaRequired' }>

/** RCU-PLT-001: tenant-branded sign-in with email + password, SSO options and MFA for elevated roles. */
export function SignInPage() {
  const { status, session, signedOutReason } = useAuth()
  const workspace = useWorkspaceParam()
  const branding = useWorkspaceBranding(workspace)

  if (status === 'restoring') return <Loading />
  if (status === 'signedIn' && session) return <Navigate to={homePath(session.user.role)} replace />

  if (!workspace || branding.isError) {
    return (
      <AuthShell>
        <WorkspaceStep initial={workspace ?? ''} notFound={branding.isError ? branding.error : null} reason={signedOutReason} />
      </AuthShell>
    )
  }
  if (branding.isPending) return <Loading />

  return (
    <AuthShell branding={branding.data}>
      <CredentialsStep branding={branding.data} reason={signedOutReason} />
    </AuthShell>
  )
}

type Reason = ReturnType<typeof useAuth>['signedOutReason']

function SignedOutNotice({ reason, minutes }: { reason: Reason; minutes: number }) {
  const { t } = useTranslation()
  if (!reason) return null
  return (
    <div className="auth-info" role="status">
      {t(`auth.signIn.reasons.${reason}`, { minutes })}
    </div>
  )
}

function WorkspaceStep({ initial, notFound, reason }: { initial: string; notFound: unknown; reason: Reason }) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [value, setValue] = useState(initial)
  const [error, setError] = useState<string | null>(null)

  const go = (slug: string) => navigate(withWorkspace('/signin', slug), { replace: true })
  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (!value.trim()) return setError(t('auth.errors.workspace'))
    go(value)
  }

  return (
    <form onSubmit={submit} noValidate>
      <h2>{t('auth.signIn.title')}</h2>
      <p className="auth-sub">{t('auth.signIn.chooseWorkspace')}</p>
      <SignedOutNotice reason={reason} minutes={DEFAULT_IDLE_MINUTES} />
      <FormError error={notFound} />
      <AuthField
        label={t('auth.fields.workspace')}
        hint={t('auth.fields.workspaceHint')}
        value={value}
        onChange={setValue}
        error={error}
        autoComplete="organization"
        autoFocus
      />
      <button type="submit" className="btn btn-primary auth-submit">{t('auth.signIn.continue')}</button>
      {features.demoPersonas && (
        <button type="button" className="btn btn-ghost auth-submit" onClick={() => go(demoTenant.slug)}>
          {t('auth.signIn.demoWorkspace')}
        </button>
      )}
      <p className="auth-alt">
        {t('auth.signIn.noAccount')} <Link to="/signup">{t('auth.signIn.createWorkspace')}</Link>
      </p>
    </form>
  )
}

function CredentialsStep({ branding, reason }: { branding: WorkspaceBranding; reason: Reason }) {
  const { t } = useTranslation()
  const complete = useCompleteSignIn()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<{ email?: string; password?: string }>({})
  const [failure, setFailure] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  const [challenge, setChallenge] = useState<MfaChallenge | null>(null)

  const handle = async (attempt: () => Promise<SignInResult>) => {
    setBusy(true)
    setFailure(null)
    try {
      const result = await attempt()
      if (result.status === 'signedIn') complete(result.session)
      else setChallenge(result)
    } catch (e) {
      setFailure(e)
    } finally {
      setBusy(false)
    }
  }

  const submit = (e: FormEvent) => {
    e.preventDefault()
    const next = {
      email: isValidEmail(email) ? undefined : t('auth.errors.email'),
      password: password ? undefined : t('auth.errors.password'),
    }
    setErrors(next)
    if (next.email || next.password) return
    void handle(() => api.signIn({ workspace: branding.slug, email, password }))
  }

  if (challenge) return <MfaStep challenge={challenge} onBack={() => setChallenge(null)} />

  return (
    <>
      <form onSubmit={submit} noValidate>
        <h2>{t('auth.signIn.subtitle', { workspace: branding.name })}</h2>
        <SignedOutNotice reason={reason} minutes={branding.sessionIdleMinutes} />
        <FormError error={failure} />
        <AuthField label={t('auth.fields.email')} type="email" value={email} onChange={setEmail} error={errors.email} autoComplete="username" autoFocus />
        <PasswordField label={t('auth.fields.password')} value={password} onChange={setPassword} error={errors.password} autoComplete="current-password" />
        <div className="auth-row">
          <Link to={withWorkspace('/forgot-password', branding.slug)}>{t('auth.signIn.forgot')}</Link>
        </div>
        <button type="submit" className="btn btn-primary auth-submit" disabled={busy}>
          {busy ? t('auth.signIn.submitting') : t('auth.signIn.submit')}
        </button>
      </form>

      {branding.ssoProviders.length > 0 && (
        <>
          <div className="auth-divider"><span>{t('auth.signIn.or')}</span></div>
          <div className="auth-sso">
            {branding.ssoProviders.map((p: SsoProvider) => (
              <button key={p} type="button" className="btn btn-ghost" disabled={busy} onClick={() => void handle(() => api.signInWithSso(branding.slug, p))}>
                {t(`auth.signIn.sso.${p}`)}
              </button>
            ))}
          </div>
        </>
      )}

      <div className="auth-alt">
        <p>{t('auth.signIn.staffNote')}</p>
        <p>
          {t('auth.signIn.candidatePrompt', { workspace: branding.name })}{' '}
          <Link to={withWorkspace('/signup/candidate', branding.slug)}>{t('auth.signIn.candidateLink')}</Link>
        </p>
        <p>
          <Link to="/signin?workspace=" replace>
            {t('auth.signIn.switchWorkspace', { workspace: branding.name })}
          </Link>
        </p>
      </div>

      <DemoPersonas workspace={branding.slug} onPick={(role) => void handle(async () => ({ status: 'signedIn', session: await api.demoSignIn(branding.slug, role) }))} />
    </>
  )
}

function MfaStep({ challenge, onBack }: { challenge: MfaChallenge; onBack: () => void }) {
  const { t } = useTranslation()
  const complete = useCompleteSignIn()
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [failure, setFailure] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!/^\d{6}$/.test(code.trim())) return setError(t('auth.errors.code'))
    setError(null)
    setBusy(true)
    try {
      complete(await api.verifyMfa(challenge.challengeId, code))
    } catch (err) {
      setFailure(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={(e) => void submit(e)} noValidate>
      <h2>{t('auth.mfa.title')}</h2>
      <p className="auth-sub">{t('auth.mfa.body', { to: challenge.deliveredTo })}</p>
      {features.demoPersonas && challenge.demoCode && <div className="auth-info">{t('auth.mfa.demoCode', { code: challenge.demoCode })}</div>}
      <FormError error={failure} />
      <AuthField label={t('auth.fields.code')} value={code} onChange={setCode} error={error} inputMode="numeric" autoComplete="one-time-code" maxLength={6} autoFocus />
      <button type="submit" className="btn btn-primary auth-submit" disabled={busy}>{t('auth.mfa.submit')}</button>
      <button type="button" className="btn btn-ghost auth-submit" onClick={onBack}>{t('auth.mfa.back')}</button>
    </form>
  )
}

/** Dev/demo only: one-click sign-in as each persona (the old persona switcher). */
function DemoPersonas({ workspace, onPick }: { workspace: string; onPick: (role: Role) => void }) {
  const { t } = useTranslation()
  const demo = useDemoAccess(workspace)
  if (!demo.data) return null
  return (
    <section className="auth-demo" aria-labelledby="demo-title">
      <h3 id="demo-title">{t('auth.demo.title')}</h3>
      <p>{t('auth.demo.body', { password: demo.data.password })}</p>
      <div className="auth-demo-list">
        {demo.data.personas.map((p) => (
          <button key={p.id} type="button" className="rm-item" onClick={() => onPick(p.role)} aria-label={t('auth.demo.as', { role: t(`common.roles.${p.role}`) })}>
            <b>{p.name}</b>
            <span>{t(`common.roles.${p.role}`)} · {p.email}</span>
          </button>
        ))}
      </div>
    </section>
  )
}
