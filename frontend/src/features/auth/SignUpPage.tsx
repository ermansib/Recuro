import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, Navigate } from 'react-router-dom'
import { api } from '../../api/client'
import { homePath } from '../../auth/permissions'
import { useAuth } from '../../auth/sessionContext'
import { useToast } from '../../components/toastContext'
import { ORG_TYPE_DEFAULTS, isValidEmail, slugify, unmetPasswordRules } from '../../domain/auth'
import type { OrgType, RegisterOrganisationInput } from '../../domain/types'
import { AuthShell } from './AuthShell'
import { AuthField, FormError, PasswordChecklist, PasswordField } from './fields'
import { useCompleteSignIn } from './useCompleteSignIn'

const ORG_TYPES: OrgType[] = ['smallBusiness', 'agency', 'enterprise']
const ADMIN_ROLES: RegisterOrganisationInput['adminRole'][] = ['hrta', 'hrhead', 'mdceo']

type Errors = Partial<Record<'orgName' | 'adminName' | 'email' | 'password' | 'acceptTerms', string>>

/** Self-service workspace creation for any kind of hiring organisation (multi-tenant onboarding). */
export function SignUpPage() {
  const { t } = useTranslation()
  const { status, session } = useAuth()
  const complete = useCompleteSignIn()
  const toast = useToast()
  const [form, setForm] = useState<RegisterOrganisationInput>({
    orgName: '',
    orgType: 'smallBusiness',
    adminName: '',
    adminRole: ORG_TYPE_DEFAULTS.smallBusiness.adminRole,
    email: '',
    password: '',
    acceptTerms: false,
  })
  const [errors, setErrors] = useState<Errors>({})
  const [failure, setFailure] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  if (status === 'signedIn' && session) return <Navigate to={homePath(session.user.role)} replace />

  const set = <K extends keyof RegisterOrganisationInput>(k: K, v: RegisterOrganisationInput[K]) => setForm((f) => ({ ...f, [k]: v }))
  const chooseType = (orgType: OrgType) => setForm((f) => ({ ...f, orgType, adminRole: ORG_TYPE_DEFAULTS[orgType].adminRole }))

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    const next: Errors = {
      orgName: form.orgName.trim() ? undefined : t('auth.errors.orgName'),
      adminName: form.adminName.trim() ? undefined : t('auth.errors.name'),
      email: isValidEmail(form.email) ? undefined : t('auth.errors.email'),
      password: unmetPasswordRules(form.password).length === 0 ? undefined : t('auth.errors.passwordPolicy'),
      acceptTerms: form.acceptTerms ? undefined : t('auth.errors.terms'),
    }
    setErrors(next)
    if (Object.values(next).some(Boolean)) return
    setBusy(true)
    setFailure(null)
    try {
      const created = await api.registerOrganisation(form)
      toast(t('auth.signUp.created', { name: created.tenant.name }), 'move')
      complete(created)
    } catch (err) {
      setFailure(err)
    } finally {
      setBusy(false)
    }
  }

  const slug = slugify(form.orgName)

  return (
    <AuthShell>
      <form onSubmit={(e) => void submit(e)} noValidate>
        <h2>{t('auth.signUp.title')}</h2>
        <p className="auth-sub">{t('auth.signUp.subtitle')}</p>
        <FormError error={failure} />

        <fieldset className="org-types">
          <legend>{t('auth.fields.orgType')}</legend>
          {ORG_TYPES.map((type) => (
            <label key={type} className={`org-type ${form.orgType === type ? 'active' : ''}`}>
              <input type="radio" name="orgType" value={type} checked={form.orgType === type} onChange={() => chooseType(type)} />
              <b>{t(`auth.orgTypes.${type}`)}</b>
              <span>{t(`auth.orgTypeHints.${type}`)}</span>
            </label>
          ))}
        </fieldset>

        <AuthField
          label={t('auth.fields.orgName')}
          value={form.orgName}
          onChange={(v) => set('orgName', v)}
          error={errors.orgName}
          hint={slug ? t('auth.signUp.workspaceUrl', { url: `/signin?workspace=${slug}` }) : undefined}
          autoComplete="organization"
          autoFocus
        />
        <AuthField label={t('auth.fields.name')} value={form.adminName} onChange={(v) => set('adminName', v)} error={errors.adminName} autoComplete="name" />
        <div className="field">
          <label htmlFor="admin-role">{t('auth.fields.adminRole')}</label>
          <select id="admin-role" value={form.adminRole} onChange={(e) => set('adminRole', e.target.value as RegisterOrganisationInput['adminRole'])}>
            {ADMIN_ROLES.map((r) => (
              <option key={r} value={r}>{t(`common.roles.${r}`)}</option>
            ))}
          </select>
        </div>
        <AuthField label={t('auth.fields.workEmail')} type="email" value={form.email} onChange={(v) => set('email', v)} error={errors.email} autoComplete="email" />
        <PasswordField label={t('auth.fields.password')} value={form.password} onChange={(v) => set('password', v)} error={errors.password} autoComplete="new-password" />
        <PasswordChecklist password={form.password} />
        <label className="chk-line">
          <input type="checkbox" checked={form.acceptTerms} onChange={(e) => set('acceptTerms', e.target.checked)} aria-invalid={!!errors.acceptTerms} />
          <span>{t('auth.signUp.terms')}</span>
        </label>
        {errors.acceptTerms && <span className="err-text" role="alert">{errors.acceptTerms}</span>}
        <button type="submit" className="btn btn-gold auth-submit" disabled={busy}>
          {busy ? t('auth.signUp.submitting') : t('auth.signUp.submit')}
        </button>
        <p className="auth-alt">
          {t('auth.signUp.haveAccount')} <Link to="/signin">{t('auth.signUp.signInLink')}</Link>
        </p>
      </form>
    </AuthShell>
  )
}
