import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { useSetTenantSuspended, useTenants, useUpdateTenant } from '../../api/hooks'
import { TENANT_PLANS, type Tenant, type TenantPlan } from '../../api/types'
import { ErrorMessage, PageHeader, QueryState } from '../../components/ui'
import { CreateTenantForm } from './CreateTenantForm'

export function TenantsPage() {
  const { t } = useTranslation()
  const tenants = useTenants()

  return (
    <>
      <PageHeader title={t('tenants.title')} subtitle={t('tenants.subtitle')} />
      <section className="card" aria-labelledby="tenant-list">
        <h2 id="tenant-list" className="sr-only">
          {t('tenants.title')}
        </h2>
        <QueryState isLoading={tenants.isLoading} error={tenants.error}>
          {tenants.data?.length ? <TenantTable tenants={tenants.data} /> : <p className="muted">{t('tenants.empty')}</p>}
        </QueryState>
      </section>
      <CreateTenantForm />
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
              <th scope="col">{t('tenants.columns.kind')}</th>
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
                  <td className="hex">{tenant.slug}</td>
                  <td>{t(`common.kind.${tenant.kind}`)}</td>
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
                    <span className="hex">
                      {tenant.themePresetKey} · {t(`common.mode.${tenant.themeMode}`)}
                    </span>{' '}
                    <Link to={`/platform/tenant-themes?tenant=${encodeURIComponent(tenant.id)}`} aria-label={t('tenants.changeTheme', { tenant: tenant.name })}>
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
