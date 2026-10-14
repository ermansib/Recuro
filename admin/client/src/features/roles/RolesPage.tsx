import { useTranslation } from 'react-i18next'
import { PageHeader } from '../../components/ui'
import { RolesWorkspace } from './RolesWorkspace'

/** Tenant admin: the workspace's own roles, hierarchy, page access and who holds each role. */
export function RolesPage() {
  const { t } = useTranslation()
  return (
    <>
      <PageHeader title={t('roles.title')} subtitle={t('roles.subtitle')} />
      <RolesWorkspace scope={undefined} />
    </>
  )
}
