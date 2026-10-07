import { useTranslation } from 'react-i18next'
import { Card, PageHead } from '../components/ui'

export function Phase1Placeholder({ screen, stories }: { screen: 'onboarding' | 'vendors' | 'reports' | 'internalCareers'; stories: string }) {
  const { t } = useTranslation()
  return (
    <div className="screen">
      <PageHead title={t('common.phase1.title', { screen: t(`common.phase1.screens.${screen}`) })} />
      <Card style={{ padding: 22 }}>
        <p style={{ fontSize: 13, marginBottom: 8 }}>{t('common.phase1.body')}</p>
        <p className="subnote">{t('common.phase1.stories', { stories })}</p>
      </Card>
    </div>
  )
}
