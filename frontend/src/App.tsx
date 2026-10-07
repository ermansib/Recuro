import { Navigate, Route, Routes } from 'react-router-dom'
import { can, homePath } from './auth/permissions'
import { useSession } from './auth/sessionContext'
import { Layout } from './components/Layout'
import { ApprovalsPage } from './features/approvals/ApprovalsPage'
import { AssessmentPage } from './features/assessment/AssessmentPage'
import { BgvPage } from './features/bgv/BgvPage'
import { CareersPage } from './features/careers/CareersPage'
import { DashboardPage } from './features/dashboard/DashboardPage'
import { JdBuilderPage } from './features/jd/JdBuilderPage'
import { MrfWizardPage } from './features/mrf/MrfWizardPage'
import { OfferPage } from './features/offer/OfferPage'
import { Phase1Placeholder } from './features/Phase1Placeholder'
import { PipelinePage } from './features/pipeline/PipelinePage'
import type { ReactElement } from 'react'

/** Internal screens are for HR personas; employees and candidates are sent to their portal. */
function Internal({ children }: { children: ReactElement }) {
  const { user } = useSession()
  return can(user.role, 'internal.view') ? children : <Navigate to={homePath(user.role)} replace />
}

export function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Internal><DashboardPage /></Internal>} />
        <Route path="mrf/new" element={<Internal><MrfWizardPage /></Internal>} />
        <Route path="jd" element={<Internal><JdBuilderPage /></Internal>} />
        <Route path="approvals" element={<Internal><ApprovalsPage /></Internal>} />
        <Route path="pipeline" element={<Internal><PipelinePage /></Internal>} />
        <Route path="assessment" element={<Internal><AssessmentPage /></Internal>} />
        <Route path="offer" element={<Internal><OfferPage /></Internal>} />
        <Route path="bgv" element={<Internal><BgvPage /></Internal>} />
        <Route path="onboarding" element={<Internal><Phase1Placeholder screen="onboarding" stories="RCU-ONB-001..006" /></Internal>} />
        <Route path="vendors" element={<Internal><Phase1Placeholder screen="vendors" stories="RCU-VEN-001..005" /></Internal>} />
        <Route path="reports" element={<Internal><Phase1Placeholder screen="reports" stories="RCU-RPT-001..006" /></Internal>} />
        <Route path="internal-careers" element={<Phase1Placeholder screen="internalCareers" stories="RCU-EMP-001..005" />} />
        <Route path="careers" element={<CareersPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  )
}
