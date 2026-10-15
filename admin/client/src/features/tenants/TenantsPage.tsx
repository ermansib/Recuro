import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { useSetTenantSuspended, useTenants, useUpdateTenant } from '../../api/hooks'
import { TENANT_PLANS, type Tenant, type TenantPlan } from '../../api/types'
import { ErrorMessage, PageHeader, QueryState } from '../../components/ui'
import { CreateTenantForm } from './CreateTenantForm'

const ADD_FORM_ID = 'add-workspace'

export function TenantsPage() {
  const { t } = useTranslation()
  const tenants = useTenants()
  const [adding, setAdding] = useState(false)
  // With no workspaces yet, adding one is the only thing to do here.
  const showForm = adding || tenants.data?.length === 0

  const startAdding = () => {
    setAdding(true)
    // Move focus to the form's heading once it is on screen.
    requestAnimationFrame(() => document.getElementById(ADD_FORM_ID)?.querySelector<HTMLElement>('h2')?.focus())
  }

  return (
    <>
      <PageHeader
        title={t('tenants.title')}
        subtitle={t('tenants.subtitle')}
        action={
          !showForm && (
            <button type="button" className="btn" onClick={startAdding}>
              {t('tenants.add')}
            </button>
          )
        }
      />
      {showForm && (
        <div id={ADD_FORM_ID}>
          <CreateTenantForm onClose={tenants.data?.length ? () => setAdding(false) : undefined} />
        </div>
      )}
      <section className="card" aria-labelledby="tenant-list">
        <h2 id="tenant-list" className="sr-only">
          {t('tenants.title')}
        </h2>
        <QueryState isLoading={tenants.isLoading} error={tenants.error}>
          {tenants.data?.length ? <TenantTable tenants={tenants.data} /> : <p className="muted">{t('tenants.empty')}</p>}
        </QueryState>
      </section>
    </>
  )
}

function TenantTable({ tenants }: { tenants: Tenant[] }) {
  const { t } = useTranslation()
  const updateTenant = useUpdateTenant()
  const setSuspended = useSetTenantSuspended()

  const changePlan = (tenant: Tenant, plan: TenantPlan) =>
    updateTenant.mutate({ id: tenant.id, request: { name: tenant.name, plan, customDomain: tenant.customDomain } })

  return (
    <>
      <ErrorMessage error={updateTenant.error ?? setSuspended.error} />
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th scope="col">{t('tenants.columns.name')}</th>
              <th scope="col">{t('tenants.columns.slug')}</th>
              <th scope="col">{t('tenants.columns.orgType')}</th>
              <th scope="col">{t('tenants.columns.plan')}</th>
              <th scope="col">{t('tenants.columns.domain')}</th>
              <th scope="col">{t('tenants.columns.theme')}</th>
              <th scope="col">{t('tenants.columns.status')}</th>
              <th scope="col">{t('tenants.columns.actions')}</th>
            </tr>
          </thead>
          <tbody>
            {tenants.map((tenant) => {
              const suspended = tenant.status === 'suspended'
              return (
                <tr key={tenant.id}>
                  <th scope="row">{tenant.name}</th>
                  <td className="hex slug">{tenant.slug}</td>
                  <td>{t(`workspaceForm.orgTypes.${tenant.orgType}`)}</td>
                  <td>
                    <select
                      className="input"
                      aria-label={`${t('tenants.columns.plan')}: ${tenant.name}`}
                      value={tenant.plan}
                      onChange={(event) => changePlan(tenant, event.target.value as TenantPlan)}
                    >
                      {TENANT_PLANS.map((plan) => (
                        <option key={plan} value={plan}>
                          {t(`common.plan.${plan}`)}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td>{tenant.customDomain ?? '—'}</td>
                  <td>
                    <span className="theme-cell">
                      <span className="hex nowrap">{tenant.themePresetKey}</span>
                      <span className="muted nowrap">{t(`common.mode.${tenant.themeMode}`)}</span>
                    </span>
                    <Link to={`/platform/workspace-themes?workspace=${encodeURIComponent(tenant.id)}`} aria-label={t('tenants.changeTheme', { tenant: tenant.name })}>
                      {t('tenants.change')}
                    </Link>
                  </td>
                  <td>
                    <span className={`badge ${suspended ? 'off' : 'ok'}`}>{t(`common.status.${tenant.status}`)}</span>
                  </td>
                  <td>
                    <button
                      type="button"
                      className="btn secondary small"
                      disabled={setSuspended.isPending}
                      onClick={() => setSuspended.mutate({ id: tenant.id, suspended: !suspended })}
                    >
                      {suspended ? t('tenants.activate') : t('tenants.suspend')}
                    </button>
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </>
  )
}
