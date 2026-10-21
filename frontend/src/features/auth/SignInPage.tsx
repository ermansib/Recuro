import { useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, Navigate, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
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
import { useCompleteSignIn, type SignInRedirectState } from './useCompleteSignIn'
import { useWorkspaceParam, withWorkspace } from './useWorkspace'

type MfaChallenge = Extract<SignInResult, { status: 'mfaRequired' }>
type Reason = ReturnType<typeof useAuth>['signedOutReason']

/** The three steps people see. For workspaces on the server the password step is the identity provider's page, themed to match. */
const STEPS = ['workspace', 'email', 'password'] as const
type StepKey = (typeof STEPS)[number]
type Direction = 'forward' | 'back'

/**
 * RCU-PLT-001: one card that walks through workspace → email → password, with a back link at every step.
 * The workspace's branding (admin portal theme) applies from the email step on.
 */
export function SignInPage() {
  const { status, session, signedOutReason } = useAuth()
  const workspace = useWorkspaceParam()
  const branding = useWorkspaceBranding(workspace)

  if (status === 'restoring') return <Loading />
  if (status === 'signedIn' && session) return <Navigate to={homePath(session.user.role)} replace />

  const chooseWorkspace = !workspace || branding.isError
  return (
    <AuthShell branding={chooseWorkspace ? undefined : branding.data}>
      {chooseWorkspace ? (
        <StepFrame key="workspace" direction="forward">
          <WorkspaceStep initial={workspace ?? ''} notFound={branding.isError ? branding.error : null} reason={signedOutReason} />
        </StepFrame>
      ) : branding.isPending ? (
        <FindingWorkspace />
      ) : (
        <AccountSteps branding={branding.data} reason={signedOutReason} />
      )}
    </AuthShell>
  )
}

/** Keyed by step at each use, so it re-mounts on every step change and the new step slides in from the side it logically comes from. */
function StepFrame({ direction, leaving = false, children }: { direction: Direction; leaving?: boolean; children: ReactNode }) {
  return (
    <div className={`auth-step ${direction}${leaving ? ' leaving' : ''}`}>
      {children}
    </div>
  )
}

function StepProgress({ current }: { current: StepKey }) {
  const { t } = useTranslation()
  const at = STEPS.indexOf(current)
  return (
    <ol className="auth-steps" aria-label={t('auth.steps.label', { n: at + 1, total: STEPS.length })}>
      {STEPS.map((step, i) => (
        <li key={step} className={i < at ? 'done' : i === at ? 'on' : ''} aria-current={i === at ? 'step' : undefined}>
          <span className="auth-steps-dot" aria-hidden="true" />
          <span className="auth-steps-name">{t(`auth.steps.${step}`)}</span>
        </li>
      ))}
    </ol>
  )
}

function BackButton({ onClick, label }: { onClick: () => void; label: string }) {
  return (
    <button type="button" className="auth-back" onClick={onClick}>
      <svg viewBox="0 0 20 20" width="16" height="16" aria-hidden="true">
        <path d="M12.5 4.5 7 10l5.5 5.5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
      {label}
    </button>
  )
}

/** Who is signing in where, with a way to change it: the workspace from the email step on, the email on the password step. */
function Identity({ workspace, email, onChangeWorkspace, onChangeEmail }: { workspace: WorkspaceBranding; email?: string; onChangeWorkspace?: () => void; onChangeEmail?: () => void }) {
  const { t } = useTranslation()
  return (
    <div className="auth-who">
      <div className="auth-chip">
        <span className="auth-chip-mark" aria-hidden="true">{workspace.name.trim().charAt(0).toUpperCase()}</span>
        <span className="auth-chip-text">
          <small>{t('auth.steps.workspace')}</small>
          <b>{workspace.name}</b>
        </span>
        {onChangeWorkspace && (
          <button type="button" className="auth-chip-change" onClick={onChangeWorkspace} aria-label={t('auth.signIn.changeWorkspace')}>
            {t('auth.signIn.change')}
          </button>
        )}
      </div>
      {email && (
        <div className="auth-chip">
          <span className="auth-chip-mark person" aria-hidden="true">{email.charAt(0).toUpperCase()}</span>
          <span className="auth-chip-text">
            <small>{t('auth.steps.email')}</small>
            <b>{email}</b>
          </span>
          {onChangeEmail && (
            <button type="button" className="auth-chip-change" onClick={onChangeEmail} aria-label={t('auth.signIn.changeEmail')}>
              {t('auth.signIn.change')}
            </button>
          )}
        </div>
      )}
    </div>
  )
}

function SubmitButton({ busy, label, busyLabel }: { busy: boolean; label: string; busyLabel: string }) {
  return (
    <button type="submit" className="btn btn-primary auth-submit" disabled={busy} aria-busy={busy}>
      {busy && <span className="auth-spinner" aria-hidden="true" />}
      {busy ? busyLabel : label}
    </button>
  )
}

function SignedOutNotice({ reason, minutes }: { reason: Reason; minutes: number }) {
  const { t } = useTranslation()
  if (!reason) return null
  return (
    <div className="auth-info" role="status">
      {t(`auth.signIn.reasons.${reason}`, { minutes })}
    </div>
  )
}

function FindingWorkspace() {
  const { t } = useTranslation()
  return (
    <div className="auth-finding" role="status">
      <span className="auth-spinner dark" aria-hidden="true" />
      {t('auth.signIn.findingWorkspace')}
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
      <StepProgress current="workspace" />
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

/** Email, then password (or the identity provider's password page), then a second factor when the role needs one. */
function AccountSteps({ branding, reason }: { branding: WorkspaceBranding; reason: Reason }) {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const [email, setEmail] = useState(() => params.get('email')?.trim() ?? '')
  const [step, setStep] = useState<'email' | 'password' | 'mfa'>('email')
  const [direction, setDirection] = useState<Direction>('forward')
  const [challenge, setChallenge] = useState<MfaChallenge | null>(null)
  const [leaving, setLeaving] = useState(false)

  const goTo = (next: typeof step, dir: Direction) => {
    setDirection(dir)
    setStep(next)
  }
  const changeWorkspace = () => navigate('/signin?workspace=', { replace: true })

  if (step === 'mfa' && challenge) {
    return (
      <StepFrame key="mfa" direction="forward">
        <MfaStep challenge={challenge} onBack={() => goTo('email', 'back')} />
      </StepFrame>
    )
  }
  if (step === 'password') {
    return (
      <StepFrame key="password" direction={direction}>
        <PasswordStep
          branding={branding}
          email={email}
          onBack={() => goTo('email', 'back')}
          onChallenge={(c) => {
            setChallenge(c)
            goTo('mfa', 'forward')
          }}
        />
      </StepFrame>
    )
  }
  return (
    <StepFrame key="email" direction={direction} leaving={leaving}>
      <EmailStep
        branding={branding}
        reason={reason}
        email={email}
        onEmail={setEmail}
        onBack={changeWorkspace}
        onLeaving={setLeaving}
        onNext={() => goTo('password', 'forward')}
        onChallenge={(c) => {
          setChallenge(c)
          goTo('mfa', 'forward')
        }}
      />
    </StepFrame>
  )
}

interface EmailStepProps {
  branding: WorkspaceBranding
  reason: Reason
  email: string
  onEmail: (email: string) => void
  onBack: () => void
  onNext: () => void
  onLeaving: (leaving: boolean) => void
  onChallenge: (challenge: MfaChallenge) => void
}

function EmailStep({ branding, reason, email, onEmail, onBack, onNext, onLeaving, onChallenge }: EmailStepProps) {
  const { t } = useTranslation()
  const location = useLocation()
  const complete = useCompleteSignIn()
  const returnTo = (location.state as SignInRedirectState | null)?.from
  const redirect = branding.signInFlow === 'redirect'
  const [error, setError] = useState<string | null>(null)
  const [failure, setFailure] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  const attempt = async (run: () => Promise<SignInResult>) => {
    setBusy(true)
    setFailure(null)
    try {
      const result = await run()
      if (result.status === 'signedIn') complete(result.session)
      else if (result.status === 'mfaRequired') onChallenge(result)
    } catch (e) {
      setFailure(e)
    } finally {
      setBusy(false)
    }
  }

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!isValidEmail(email)) return setError(t('auth.errors.email'))
    setError(null)
    if (!redirect) return onNext()
    setFailure(null)
    setBusy(true)
    onLeaving(true)
    try {
      // On success the browser leaves for the password page, so `busy` stays on.
      await api.signIn({ workspace: branding.slug, email, password: '', returnTo })
    } catch (err) {
      setFailure(err)
      setBusy(false)
      onLeaving(false)
    }
  }

  return (
    <>
      <form onSubmit={(e) => void submit(e)} noValidate>
        <BackButton onClick={onBack} label={t('auth.signIn.back')} />
        <StepProgress current="email" />
        <h2>{t('auth.signIn.subtitle', { workspace: branding.name })}</h2>
        <p className="auth-sub">{t('auth.signIn.emailSub')}</p>
        <Identity workspace={branding} onChangeWorkspace={onBack} />
        <SignedOutNotice reason={reason} minutes={branding.sessionIdleMinutes} />
        <FormError error={failure} />
        <AuthField label={t('auth.fields.email')} type="email" value={email} onChange={onEmail} error={error} autoComplete="username" inputMode="email" autoFocus />
        <SubmitButton busy={busy} label={t('auth.signIn.continue')} busyLabel={t('auth.signIn.continuing')} />
      </form>

      {!redirect && branding.ssoProviders.length > 0 && (
        <>
          <div className="auth-divider"><span>{t('auth.signIn.or')}</span></div>
          <div className="auth-sso">
            {branding.ssoProviders.map((p: SsoProvider) => (
              <button key={p} type="button" className="btn btn-ghost" disabled={busy} onClick={() => void attempt(() => api.signInWithSso(branding.slug, p))}>
                {t(`auth.signIn.sso.${p}`)}
              </button>
            ))}
          </div>
        </>
      )}

      <div className="auth-alt">
        <p>{t('auth.signIn.staffNote')}</p>
        {!redirect && (
          <p>
            {t('auth.signIn.candidatePrompt', { workspace: branding.name })}{' '}
            <Link to={withWorkspace('/signup/candidate', branding.slug)}>{t('auth.signIn.candidateLink')}</Link>
          </p>
        )}
      </div>

      {!redirect && (
        <DemoPersonas workspace={branding.slug} onPick={(role) => void attempt(async () => ({ status: 'signedIn', session: await api.demoSignIn(branding.slug, role) }))} />
      )}
    </>
  )
}

function PasswordStep({ branding, email, onBack, onChallenge }: { branding: WorkspaceBranding; email: string; onBack: () => void; onChallenge: (challenge: MfaChallenge) => void }) {
  const { t } = useTranslation()
  const complete = useCompleteSignIn()
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [failure, setFailure] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  // Re-mounts the field after a refused attempt, so it is empty and focused for the next try.
  const [attempt, setAttempt] = useState(0)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!password) return setError(t('auth.errors.password'))
    setError(null)
    setFailure(null)
    setBusy(true)
    try {
      const result = await api.signIn({ workspace: branding.slug, email, password })
      if (result.status === 'signedIn') complete(result.session)
      else if (result.status === 'mfaRequired') onChallenge(result)
    } catch (err) {
      setFailure(err)
      setPassword('')
      setAttempt((n) => n + 1)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={(e) => void submit(e)} noValidate>
      <BackButton onClick={onBack} label={t('auth.signIn.back')} />
      <StepProgress current="password" />
      <h2>{t('auth.signIn.passwordTitle')}</h2>
      <p className="auth-sub">{t('auth.signIn.passwordSub')}</p>
      <Identity workspace={branding} email={email} onChangeEmail={onBack} />
      {/* Lets password managers pair the password with the account. */}
      <input className="sr-only" type="email" name="username" autoComplete="username" value={email} readOnly tabIndex={-1} aria-hidden="true" />
      <FormError error={failure} />
      <PasswordField key={attempt} label={t('auth.fields.password')} value={password} onChange={setPassword} error={error} autoComplete="current-password" autoFocus />
      <div className="auth-row">
        <Link to={withWorkspace('/forgot-password', branding.slug)}>{t('auth.signIn.forgot')}</Link>
      </div>
      <SubmitButton busy={busy} label={t('auth.signIn.submit')} busyLabel={t('auth.signIn.submitting')} />
    </form>
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
      <SubmitButton busy={busy} label={t('auth.mfa.submit')} busyLabel={t('auth.signIn.submitting')} />
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
