import { useId, useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import {
  WORKSPACE_FORM,
  WORKSPACE_FORM_LIMITS,
  emptyWorkspaceForm,
  validateWorkspaceForm,
  withOrgType,
  workspaceSlug,
  type WorkspaceFormField,
  type WorkspaceOwnerRole,
} from '@workspace-form'
import { useCreateTenant } from '../../api/hooks'
import { TENANT_PLANS, type CreateTenantRequest, type TenantPlan } from '../../api/types'
import { TemporaryPasswordField } from '../../components/TemporaryPasswordField'
import { ErrorMessage, StatusMessage } from '../../components/ui'

const empty = (): CreateTenantRequest => ({ ...emptyWorkspaceForm(), plan: 'starter', customDomain: null })

interface CreateTenantFormProps {
  /** Shows a close button; the form stays usable without it. */
  onClose?: () => void
}

/**
 * Creates a workspace from the platform console. The workspace and owner fields are the shared create-workspace
 * form (admin/contracts/workspace-form), the same as the main portal's sign-up page; plan and custom domain are
 * the platform console's own settings.
 */
export function CreateTenantForm({ onClose }: CreateTenantFormProps) {
  const { t } = useTranslation()
  const ids = useId()
  const createTenant = useCreateTenant()
  const [form, setForm] = useState<CreateTenantRequest>(empty)
  // Errors show once the form has been sent, then update as the fields are corrected.
  const [checked, setChecked] = useState(false)
  const [created, setCreated] = useState<string | null>(null)

  const id = (field: string) => `${ids}-${field}`
  const errors = checked ? validateWorkspaceForm(form) : {}
  const invalidFields = WORKSPACE_FORM.fields.filter((field) => errors[field])
  const update = (patch: Partial<CreateTenantRequest>) => setForm((current) => ({ ...current, ...patch }))

  const errorOf = (field: WorkspaceFormField) => {
    const key = errors[field]
    return key ? (
      <small id={id(`${field}-err`)} className="err-text" role="alert">
        {t(key, WORKSPACE_FORM_LIMITS[field])}
      </small>
    ) : null
  }
  const described = (field: WorkspaceFormField, hint?: string) => {
    const refs = [errors[field] && id(`${field}-err`), hint].filter(Boolean).join(' ')
    return { 'aria-invalid': !!errors[field], 'aria-describedby': refs || undefined }
  }

  const submit = (event: FormEvent) => {
    event.preventDefault()
    setCreated(null)
    setChecked(true)
    const firstInvalid = WORKSPACE_FORM.fields.find((field) => validateWorkspaceForm(form)[field])
    if (firstInvalid) {
      document.getElementById(id(firstInvalid))?.focus()
      return
    }
    createTenant.mutate(
      { ...form, customDomain: form.customDomain?.trim() || null },
      {
        onSuccess: (tenant) => {
          setCreated(t('tenants.create.created', { name: tenant.name, url: `/signin?workspace=${tenant.slug}` }))
          setForm(empty())
          setChecked(false)
        },
      },
    )
  }

  const clear = () => {
    setForm(empty())
    setChecked(false)
  }

  const slug = workspaceSlug(form.orgName)

  return (
    <section className="card form-card" aria-labelledby={id('title')}>
      <header className="form-card-head">
        <div>
          <h2 id={id('title')} tabIndex={-1}>
            {t('tenants.create.title')}
          </h2>
          <p className="muted">{t('tenants.create.intro')}</p>
        </div>
        {onClose && (
          <button type="button" className="btn secondary small" onClick={onClose}>
            {t('common.close')}
          </button>
        )}
      </header>
      <ErrorMessage error={createTenant.error} />
      <StatusMessage message={created} />
      {invalidFields.length > 0 && (
        <p className="error">{t('tenants.create.fixErrors', { count: invalidFields.length })}</p>
      )}
      <form onSubmit={submit} noValidate>
        <FormBlock step={1} title={t('workspaceForm.workspaceSection')} hint={t('tenants.create.workspaceHint')}>
          <fieldset className="choice-tiles">
            <legend>{t('workspaceForm.orgType')}</legend>
            {WORKSPACE_FORM.orgTypes.map((type, index) => (
              <label key={type} className="choice-tile">
                <input
                  type="radio"
                  id={index === 0 ? id('orgType') : undefined}
                  name={id('orgType')}
                  value={type}
                  checked={form.orgType === type}
                  onChange={() => setForm((f) => withOrgType(f, type))}
                />
                <span>
                  <b>{t(`workspaceForm.orgTypes.${type}`)}</b>
                  <small>{t(`workspaceForm.orgTypeHints.${type}`)}</small>
                </span>
              </label>
            ))}
            {errorOf('orgType')}
          </fieldset>
          <div className="field">
            <label htmlFor={id('orgName')}>{t('workspaceForm.orgName')}</label>
            <input
              id={id('orgName')}
              maxLength={WORKSPACE_FORM.orgNameMaxLength}
              value={form.orgName}
              onChange={(event) => update({ orgName: event.target.value })}
              {...described('orgName', id('orgName-hint'))}
            />
            <small id={id('orgName-hint')} className={slug ? 'address-preview' : undefined}>
              {slug ? t('workspaceForm.signInAddress', { url: `/signin?workspace=${slug}` }) : t('tenants.create.orgNameHint')}
            </small>
            {errorOf('orgName')}
          </div>
        </FormBlock>

        <FormBlock step={2} title={t('workspaceForm.ownerSection')} hint={t('tenants.create.ownerHint')}>
          <div className="form-grid two">
            <div className="field">
              <label htmlFor={id('adminName')}>{t('workspaceForm.adminName')}</label>
              <input
                id={id('adminName')}
                autoComplete="off"
                maxLength={WORKSPACE_FORM.adminNameMaxLength}
                value={form.adminName}
                onChange={(event) => update({ adminName: event.target.value })}
                {...described('adminName')}
              />
              {errorOf('adminName')}
            </div>
            <div className="field">
              <label htmlFor={id('adminRole')}>{t('workspaceForm.adminRole')}</label>
              <select
                id={id('adminRole')}
                value={form.adminRole}
                onChange={(event) => update({ adminRole: event.target.value as WorkspaceOwnerRole })}
                {...described('adminRole', id('adminRole-hint'))}
              >
                {WORKSPACE_FORM.ownerRoles.map((role) => (
                  <option key={role} value={role}>
                    {t(`workspaceForm.roles.${role}`)}
                  </option>
                ))}
              </select>
              <small id={id('adminRole-hint')}>{t('workspaceForm.adminRoleHint')}</small>
              {errorOf('adminRole')}
            </div>
            <div className="field span-2">
              <label htmlFor={id('email')}>{t('workspaceForm.email')}</label>
              <input
                id={id('email')}
                type="email"
                autoComplete="off"
                value={form.email}
                onChange={(event) => update({ email: event.target.value })}
                {...described('email', id('email-hint'))}
              />
              <small id={id('email-hint')}>{t('tenants.create.emailHint')}</small>
              {errorOf('email')}
            </div>
          </div>
          <TemporaryPasswordField
            id={id('password')}
            value={form.password}
            onChange={(password) => update({ password })}
            hint={t('tenants.create.passwordHint')}
            error={errors.password}
          />
        </FormBlock>

        <FormBlock step={3} title={t('tenants.create.platformSection')} hint={t('tenants.create.platformHint')}>
          <div className="form-grid two">
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
              <label htmlFor={id('domain')}>
                {t('tenants.create.domain')} <span className="optional">{t('tenants.create.optional')}</span>
              </label>
              <input
                id={id('domain')}
                inputMode="url"
                placeholder={t('tenants.create.domainPlaceholder')}
                value={form.customDomain ?? ''}
                onChange={(event) => update({ customDomain: event.target.value })}
                aria-describedby={id('domain-hint')}
              />
              <small id={id('domain-hint')}>{t('tenants.create.domainHint')}</small>
            </div>
          </div>
        </FormBlock>

        <div className="form-footer">
          <button type="button" className="btn secondary" onClick={clear}>
            {t('tenants.create.clear')}
          </button>
          <button type="submit" className="btn" disabled={createTenant.isPending}>
            {createTenant.isPending ? t('common.saving') : t('tenants.create.submit')}
          </button>
        </div>
      </form>
    </section>
  )
}

interface FormBlockProps {
  step: number
  title: string
  hint: string
  children: ReactNode
}

/** One numbered group of the form: its title and purpose on the left, its fields on the right. */
function FormBlock({ step, title, hint, children }: FormBlockProps) {
  return (
    <div className="form-block">
      <div className="form-block-head">
        <h3>
          <span className="step" aria-hidden="true">
            {step}
          </span>
          {title}
        </h3>
        <p>{hint}</p>
      </div>
      <div className="form-block-body">{children}</div>
    </div>
  )
}
