import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router-dom'
import { useTenants } from '../../api/hooks'
import { PageHeader, QueryState } from '../../components/ui'
import { RolesWorkspace } from './RolesWorkspace'

/** Platform console: pick a workspace, then manage its roles exactly as its tenant admin would. */
export function PlatformRolesPage() {
  const { t } = useTranslation()
  const tenants = useTenants()
  const [params, setParams] = useSearchParams()
  const selected = params.get('tenant') ?? tenants.data?.[0]?.id

  return (
    <>
      <PageHeader title={t('roles.platformTitle')} subtitle={t('roles.platformSubtitle')} />
      <QueryState isLoading={tenants.isLoading} error={tenants.error}>
        <section className="card">
          <div className="field">
            <label htmlFor="roles-tenant">{t('roles.tenant')}</label>
            <select id="roles-tenant" className="input" value={selected ?? ''} onChange={(event) => setParams({ tenant: event.target.value })}>
              {tenants.data?.map((tenant) => (
                <option key={tenant.id} value={tenant.id}>
                  {tenant.name}
                </option>
              ))}
            </select>
          </div>
        </section>
        {/* Keyed so forms reset when another workspace is picked. */}
        {selected && <RolesWorkspace key={selected} scope={selected} />}
      </QueryState>
    </>
  )
}
