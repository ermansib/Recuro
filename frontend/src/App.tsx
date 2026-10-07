import { Navigate, Route, Routes } from 'react-router-dom'
import { can, homePath, type Capability } from './auth/permissions'
import { RequireAuth } from './auth/RequireAuth'
import { useSession } from './auth/sessionContext'
import { Layout } from './components/Layout'
import { ApprovalsPage } from './features/approvals/ApprovalsPage'
import { AcceptInvitePage } from './features/auth/AcceptInvitePage'
import { CandidateSignUpPage } from './features/auth/CandidateSignUpPage'
import { ForgotPasswordPage } from './features/auth/ForgotPasswordPage'
import { ResetPasswordPage } from './features/auth/ResetPasswordPage'
import { SignInPage } from './features/auth/SignInPage'
import { SignUpPage } from './features/auth/SignUpPage'
import { AssessmentPage } from './features/assessment/AssessmentPage'
import { BgvPage } from './features/bgv/BgvPage'
import { CareersPage } from './features/careers/CareersPage'
import { DashboardPage } from './features/dashboard/DashboardPage'
import { JdBuilderPage } from './features/jd/JdBuilderPage'
import { MrfWizardPage } from './features/mrf/MrfWizardPage'
import { OfferPage } from './features/offer/OfferPage'
import { Phase1Placeholder } from './features/Phase1Placeholder'
import { PipelinePage } from './features/pipeline/PipelinePage'
import { TeamPage } from './features/team/TeamPage'
import type { ReactElement } from 'react'

/** Shows the screen only to roles with the capability; everyone else goes to their own landing page. */
function Allowed({ capability, children }: { capability: Capability; children: ReactElement }) {
  const { user } = useSession()
  return can(user.role, capability) ? children : <Navigate to={homePath(user.role)} replace />
}

/** Internal screens are for HR personas; employees and candidates are sent to their portal. */
const Internal = ({ children }: { children: ReactElement }) => <Allowed capability="internal.view">{children}</Allowed>

export function App() {
  return (
    <Routes>
      <Route path="signin" element={<SignInPage />} />
      <Route path="signup" element={<SignUpPage />} />
      <Route path="signup/candidate" element={<CandidateSignUpPage />} />
      <Route path="forgot-password" element={<ForgotPasswordPage />} />
      <Route path="reset-password" element={<ResetPasswordPage />} />
      <Route path="invite/:token" element={<AcceptInvitePage />} />
      <Route element={<RequireAuth><Layout /></RequireAuth>}>
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
        <Route path="team" element={<Allowed capability="team.invite"><TeamPage /></Allowed>} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  )
}
