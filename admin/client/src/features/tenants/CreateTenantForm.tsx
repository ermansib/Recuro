import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useCreateTenant } from '../../api/hooks'
import { TENANT_KINDS, TENANT_PLANS, type CreateTenantRequest, type TenantKind, type TenantPlan } from '../../api/types'
import { ErrorMessage, StatusMessage } from '../../components/ui'

const EMPTY: CreateTenantRequest = { name: '', slug: '', kind: 'inHouse', plan: 'starter', customDomain: null }

/** Suggests a slug from the company name until the admin edits the slug themselves. */
function slugify(name: string): string {
  return name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
}

export function CreateTenantForm() {
  const { t } = useTranslation()
  const createTenant = useCreateTenant()
  const [form, setForm] = useState<CreateTenantRequest>(EMPTY)
  const [slugEdited, setSlugEdited] = useState(false)
  const [created, setCreated] = useState<string | null>(null)

  const update = (patch: Partial<CreateTenantRequest>) => setForm((current) => ({ ...current, ...patch }))

  const submit = (event: FormEvent) => {
    event.preventDefault()
    setCreated(null)
    createTenant.mutate(
      { ...form, customDomain: form.customDomain?.trim() || null },
      {
        onSuccess: (tenant) => {
          setCreated(t('tenants.create.created', { name: tenant.name }))
          setForm(EMPTY)
          setSlugEdited(false)
        },
      },
    )
  }

  return (
    <section className="card" aria-labelledby="create-tenant">
      <h2 id="create-tenant">{t('tenants.create.title')}</h2>
      <ErrorMessage error={createTenant.error} />
      <StatusMessage message={created} />
      <form onSubmit={submit}>
        <div className="form-grid">
          <div className="field">
            <label htmlFor="tenant-name">{t('tenants.create.name')}</label>
            <input
              id="tenant-name"
              required
              maxLength={120}
              value={form.name}
              onChange={(event) =>
                update({ name: event.target.value, ...(slugEdited ? {} : { slug: slugify(event.target.value) }) })
              }
            />
          </div>
          <div className="field">
            <label htmlFor="tenant-slug">{t('tenants.create.slug')}</label>
            <input
              id="tenant-slug"
              required
              maxLength={40}
              pattern="[a-z0-9](?:[a-z0-9\-]*[a-z0-9])?"
              aria-describedby="tenant-slug-hint"
              value={form.slug}
              onChange={(event) => {
                setSlugEdited(true)
                update({ slug: event.target.value })
              }}
            />
            <small id="tenant-slug-hint">{t('tenants.create.slugHint')}</small>
          </div>
          <div className="field">
            <label htmlFor="tenant-kind">{t('tenants.create.kind')}</label>
            <select id="tenant-kind" value={form.kind} onChange={(event) => update({ kind: event.target.value as TenantKind })}>
              {TENANT_KINDS.map((kind) => (
                <option key={kind} value={kind}>
                  {t(`common.kind.${kind}`)}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor="tenant-plan">{t('tenants.create.plan')}</label>
            <select id="tenant-plan" value={form.plan} onChange={(event) => update({ plan: event.target.value as TenantPlan })}>
              {TENANT_PLANS.map((plan) => (
                <option key={plan} value={plan}>
                  {t(`common.plan.${plan}`)}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor="tenant-domain">{t('tenants.create.domain')}</label>
            <input
              id="tenant-domain"
              value={form.customDomain ?? ''}
              onChange={(event) => update({ customDomain: event.target.value })}
            />
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
