import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import {
  PASSWORD_RULE_ORDER,
  WORKSPACE_FORM,
  WORKSPACE_FORM_LIMITS,
  emptyWorkspaceForm,
  unmetPasswordRules,
  validateWorkspaceForm,
  withOrgType,
  workspaceSlug,
  type WorkspaceFormErrors,
  type WorkspaceFormField,
  type WorkspaceOwnerRole,
} from '@workspace-form'
import { useCreateTenant } from '../../api/hooks'
import { TENANT_PLANS, type CreateTenantRequest, type TenantPlan } from '../../api/types'
import { ErrorMessage, StatusMessage } from '../../components/ui'

const empty = (): CreateTenantRequest => ({ ...emptyWorkspaceForm(), plan: 'starter', customDomain: null })

/**
 * Creates a workspace from the platform console. The workspace and owner fields are the shared create-workspace
 * form (admin/contracts/workspace-form), the same as the main portal's sign-up page; plan and custom domain are
 * the platform console's own settings.
 */
export function CreateTenantForm() {
  const { t } = useTranslation()
  const ids = useId()
  const createTenant = useCreateTenant()
  const [form, setForm] = useState<CreateTenantRequest>(empty)
  const [errors, setErrors] = useState<WorkspaceFormErrors>({})
  const [created, setCreated] = useState<string | null>(null)

  const update = (patch: Partial<CreateTenantRequest>) => setForm((current) => ({ ...current, ...patch }))
  const id = (field: string) => `${ids}-${field}`
  const errorOf = (field: WorkspaceFormField) => {
    const key = errors[field]
    return key ? (
      <small id={id(`${field}-err`)} className="err-text" role="alert">
        {t(key, WORKSPACE_FORM_LIMITS[field])}
      </small>
    ) : null
  }
  const invalid = (field: WorkspaceFormField) => ({
    'aria-invalid': !!errors[field],
    'aria-describedby': errors[field] ? id(`${field}-err`) : undefined,
  })

  const submit = (event: FormEvent) => {
    event.preventDefault()
    setCreated(null)
    const next = validateWorkspaceForm(form)
    setErrors(next)
    if (Object.values(next).some(Boolean)) return
    createTenant.mutate(
      { ...form, customDomain: form.customDomain?.trim() || null },
      {
        onSuccess: (tenant) => {
          setCreated(t('tenants.create.created', { name: tenant.name, url: `/signin?workspace=${tenant.slug}` }))
          setForm(empty())
        },
      },
    )
  }

  const slug = workspaceSlug(form.orgName)
  const unmet = new Set(unmetPasswordRules(form.password))

  return (
    <section className="card" aria-labelledby={id('title')}>
      <h2 id={id('title')}>{t('tenants.create.title')}</h2>
      <p className="muted">{t('tenants.create.intro')}</p>
      <ErrorMessage error={createTenant.error} />
      <StatusMessage message={created} />
      <form onSubmit={submit} noValidate>
        <h3 className="form-section">{t('workspaceForm.workspaceSection')}</h3>
        <fieldset className="role-options">
          <legend>{t('workspaceForm.orgType')}</legend>
          {WORKSPACE_FORM.orgTypes.map((type) => (
            <label key={type} className="check">
              <input type="radio" name={id('orgType')} value={type} checked={form.orgType === type} onChange={() => setForm((f) => withOrgType(f, type))} />
              <span>
                <b>{t(`workspaceForm.orgTypes.${type}`)}</b> <small>{t(`workspaceForm.orgTypeHints.${type}`)}</small>
              </span>
            </label>
          ))}
        </fieldset>
        <div className="form-grid">
          <div className="field">
            <label htmlFor={id('orgName')}>{t('workspaceForm.orgName')}</label>
            <input
              id={id('orgName')}
              maxLength={WORKSPACE_FORM.orgNameMaxLength}
              value={form.orgName}
              onChange={(event) => update({ orgName: event.target.value })}
              {...invalid('orgName')}
            />
            {slug && <small>{t('workspaceForm.signInAddress', { url: `/signin?workspace=${slug}` })}</small>}
            {errorOf('orgName')}
          </div>
        </div>

        <h3 className="form-section">{t('workspaceForm.ownerSection')}</h3>
        <div className="form-grid">
          <div className="field">
            <label htmlFor={id('adminName')}>{t('workspaceForm.adminName')}</label>
            <input
              id={id('adminName')}
              maxLength={WORKSPACE_FORM.adminNameMaxLength}
              value={form.adminName}
              onChange={(event) => update({ adminName: event.target.value })}
              {...invalid('adminName')}
            />
            {errorOf('adminName')}
          </div>
          <div className="field">
            <label htmlFor={id('adminRole')}>{t('workspaceForm.adminRole')}</label>
            <select id={id('adminRole')} value={form.adminRole} onChange={(event) => update({ adminRole: event.target.value as WorkspaceOwnerRole })}>
              {WORKSPACE_FORM.ownerRoles.map((role) => (
                <option key={role} value={role}>
                  {t(`workspaceForm.roles.${role}`)}
                </option>
              ))}
            </select>
            <small>{t('workspaceForm.adminRoleHint')}</small>
          </div>
          <div className="field">
            <label htmlFor={id('email')}>{t('workspaceForm.email')}</label>
            <input id={id('email')} type="email" value={form.email} onChange={(event) => update({ email: event.target.value })} {...invalid('email')} />
            {errorOf('email')}
          </div>
          <div className="field">
            <label htmlFor={id('password')}>{t('workspaceForm.password')}</label>
            <input
              id={id('password')}
              type="password"
              autoComplete="new-password"
              value={form.password}
              onChange={(event) => update({ password: event.target.value })}
              {...invalid('password')}
            />
            <small>{t('tenants.create.passwordHint')}</small>
            <small>{t('workspaceForm.passwordRules.title')}</small>
            <ul className="rules">
              {PASSWORD_RULE_ORDER.map((rule) => (
                <li key={rule} className={unmet.has(rule) ? '' : 'ok'}>
                  <span aria-hidden="true">{unmet.has(rule) ? '○' : '✓'}</span>{' '}
                  {t(`workspaceForm.passwordRules.${rule}`, WORKSPACE_FORM_LIMITS.password)}
                </li>
              ))}
            </ul>
            {errorOf('password')}
          </div>
        </div>

        <h3 className="form-section">{t('tenants.create.platformSection')}</h3>
        <div className="form-grid">
          <div className="field">
            <label htmlFor={id('plan')}>{t('tenants.create.plan')}</label>
            <select id={id('plan')} value={form.plan} onChange={(event) => update({ plan: event.target.value as TenantPlan })}>
              {TENANT_PLANS.map((plan) => (
                <option key={plan} value={plan}>
                  {t(`common.plan.${plan}`)}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor={id('domain')}>{t('tenants.create.domain')}</label>
            <input id={id('domain')} value={form.customDomain ?? ''} onChange={(event) => update({ customDomain: event.target.value })} />
          </div>
        </div>
        <div className="actions">
          <button type="submit" className="btn" disabled={createTenant.isPending}>
            {createTenant.isPending ? t('common.saving') : t('tenants.create.submit')}
          </button>
        </div>
      </form>
    </section>
  )
}
