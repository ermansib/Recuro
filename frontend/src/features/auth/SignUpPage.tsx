import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, Navigate } from 'react-router-dom'
import {
  WORKSPACE_FORM,
  WORKSPACE_FORM_LIMITS,
  emptyWorkspaceForm,
  validateWorkspaceForm,
  withOrgType,
  workspaceSlug,
  type WorkspaceFormErrors,
  type WorkspaceFormField,
} from '@workspace-form'
import { api } from '../../api/client'
import { homePath } from '../../auth/permissions'
import { useAuth } from '../../auth/sessionContext'
import { useToast } from '../../components/toastContext'
import type { RegisterOrganisationInput } from '../../domain/types'
import { AuthShell } from './AuthShell'
import { AuthField, FormError, PasswordChecklist, PasswordField } from './fields'
import { useCompleteSignIn } from './useCompleteSignIn'

type Errors = WorkspaceFormErrors & { acceptTerms?: string }

/**
 * Self-service workspace creation for any kind of hiring organisation (RCU-PLT-001). The fields, labels,
 * defaults and rules are the shared create-workspace form (admin/contracts/workspace-form), the same one the
 * admin portal's platform console uses; only the terms checkbox is this page's own.
 */
export function SignUpPage() {
  const { t } = useTranslation()
  const { status, session } = useAuth()
  const complete = useCompleteSignIn()
  const toast = useToast()
  const [form, setForm] = useState<RegisterOrganisationInput>(() => ({ ...emptyWorkspaceForm(), acceptTerms: false }))
  const [errors, setErrors] = useState<Errors>({})
  const [failure, setFailure] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  if (status === 'signedIn' && session) return <Navigate to={homePath(session.user.role)} replace />

  const set = <K extends keyof RegisterOrganisationInput>(k: K, v: RegisterOrganisationInput[K]) => setForm((f) => ({ ...f, [k]: v }))
  const errorText = (field: WorkspaceFormField) => {
    const key = errors[field]
    return key ? t(key, WORKSPACE_FORM_LIMITS[field]) : undefined
  }

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    const next: Errors = { ...validateWorkspaceForm(form), ...(form.acceptTerms ? {} : { acceptTerms: 'auth.errors.terms' }) }
    setErrors(next)
    if (Object.values(next).some(Boolean)) return
    setBusy(true)
    setFailure(null)
    try {
      const result = await api.registerOrganisation(form)
      // A workspace saved on the server sends the owner to sign in, so the page is left with `busy` on.
      if (result.status !== 'signedIn') return
      toast(t('auth.signUp.created', { name: result.session.tenant.name }), 'move')
      complete(result.session)
      setBusy(false)
    } catch (err) {
      setFailure(err)
      setBusy(false)
    }
  }

  const slug = workspaceSlug(form.orgName)

  return (
    <AuthShell>
      <form onSubmit={(e) => void submit(e)} noValidate>
        <h2>{t('auth.signUp.title')}</h2>
        <p className="auth-sub">{t('auth.signUp.subtitle')}</p>
        <FormError error={failure} />

        <h3 className="auth-section">{t('workspaceForm.workspaceSection')}</h3>
        <fieldset className="org-types">
          <legend>{t('workspaceForm.orgType')}</legend>
          {WORKSPACE_FORM.orgTypes.map((type) => (
            <label key={type} className={`org-type ${form.orgType === type ? 'active' : ''}`}>
              <input type="radio" name="orgType" value={type} checked={form.orgType === type} onChange={() => setForm((f) => withOrgType(f, type))} />
              <b>{t(`workspaceForm.orgTypes.${type}`)}</b>
              <span>{t(`workspaceForm.orgTypeHints.${type}`)}</span>
            </label>
          ))}
        </fieldset>
        <AuthField
          label={t('workspaceForm.orgName')}
          value={form.orgName}
          onChange={(v) => set('orgName', v)}
          error={errorText('orgName')}
          hint={slug ? t('workspaceForm.signInAddress', { url: `/signin?workspace=${slug}` }) : undefined}
          maxLength={WORKSPACE_FORM.orgNameMaxLength}
          autoComplete="organization"
          autoFocus
        />

        <h3 className="auth-section">{t('workspaceForm.ownerSection')}</h3>
        <p className="auth-sub">{t('auth.signUp.ownerHint')}</p>
        <AuthField
          label={t('workspaceForm.adminName')}
          value={form.adminName}
          onChange={(v) => set('adminName', v)}
          error={errorText('adminName')}
          maxLength={WORKSPACE_FORM.adminNameMaxLength}
          autoComplete="name"
        />
        <div className="field">
          <label htmlFor="admin-role">{t('workspaceForm.adminRole')}</label>
          <select id="admin-role" value={form.adminRole} onChange={(e) => set('adminRole', e.target.value as RegisterOrganisationInput['adminRole'])}>
            {WORKSPACE_FORM.ownerRoles.map((r) => (
              <option key={r} value={r}>{t(`workspaceForm.roles.${r}`)}</option>
            ))}
          </select>
        </div>
        <AuthField label={t('workspaceForm.email')} type="email" value={form.email} onChange={(v) => set('email', v)} error={errorText('email')} autoComplete="email" />
        <PasswordField label={t('workspaceForm.password')} value={form.password} onChange={(v) => set('password', v)} error={errorText('password')} autoComplete="new-password" />
        <PasswordChecklist password={form.password} />
        <label className="chk-line">
          <input type="checkbox" checked={form.acceptTerms} onChange={(e) => set('acceptTerms', e.target.checked)} aria-invalid={!!errors.acceptTerms} />
          <span>{t('auth.signUp.terms')}</span>
        </label>
        {errors.acceptTerms && <span className="err-text" role="alert">{t(errors.acceptTerms)}</span>}
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
