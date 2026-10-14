import { Navigate, Route, Routes } from 'react-router-dom'
import { homePath, useCan, useCanOpen, type Capability } from './auth/permissions'
import { RequireAuth } from './auth/RequireAuth'
import { useSession } from './auth/sessionContext'
import { Layout } from './components/Layout'
import { NAV_ITEMS } from './components/nav'
import { ApprovalsPage } from './features/approvals/ApprovalsPage'
import { AcceptInvitePage } from './features/auth/AcceptInvitePage'
import { CandidateSignUpPage } from './features/auth/CandidateSignUpPage'
import { ForgotPasswordPage } from './features/auth/ForgotPasswordPage'
import { ResetPasswordPage } from './features/auth/ResetPasswordPage'
import { SignInCallbackPage } from './features/auth/SignInCallbackPage'
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

/**
 * Shows the screen only to roles allowed to open it; everyone else goes to the first screen they may open.
 * When the workspace configured the signed-in role in the admin portal, its pages decide (`screen`);
 * otherwise the role needs `capability` (FRD §3.2).
 */
function Allowed({ capability, screen, children }: { capability: Capability; screen?: string; children: ReactElement }) {
  const { user } = useSession()
  const can = useCan()
  const canOpen = useCanOpen()
  const allowed = (screen === undefined ? null : canOpen(screen)) ?? can(capability)
  if (allowed) return children
  const landing = NAV_ITEMS.find((n) => n.screen !== screen && (canOpen(n.screen) ?? n.roles.includes(user.role)))
  // The public careers site has no guard, so a role with no screens at all can't loop between redirects.
  return <Navigate to={landing?.to ?? (screen === 'dashboard' ? '/careers' : homePath(user.role))} replace />
}

/** Internal screens are for HR personas; employees and candidates are sent to their portal. */
const Internal = ({ screen, children }: { screen: string; children: ReactElement }) => (
  <Allowed capability="internal.view" screen={screen}>{children}</Allowed>
)

export function App() {
  return (
    <Routes>
      <Route path="signin" element={<SignInPage />} />
      <Route path="auth/callback" element={<SignInCallbackPage />} />
      <Route path="signup" element={<SignUpPage />} />
      <Route path="signup/candidate" element={<CandidateSignUpPage />} />
      <Route path="forgot-password" element={<ForgotPasswordPage />} />
      <Route path="reset-password" element={<ResetPasswordPage />} />
      <Route path="invite/:token" element={<AcceptInvitePage />} />
      <Route element={<RequireAuth><Layout /></RequireAuth>}>
        <Route index element={<Internal screen="dashboard"><DashboardPage /></Internal>} />
        <Route path="mrf/new" element={<Internal screen="mrf"><MrfWizardPage /></Internal>} />
        <Route path="jd" element={<Internal screen="jd"><JdBuilderPage /></Internal>} />
        <Route path="approvals" element={<Internal screen="approvals"><ApprovalsPage /></Internal>} />
        <Route path="pipeline" element={<Internal screen="pipeline"><PipelinePage /></Internal>} />
        <Route path="assessment" element={<Internal screen="assessment"><AssessmentPage /></Internal>} />
        <Route path="offer" element={<Internal screen="offer"><OfferPage /></Internal>} />
        <Route path="bgv" element={<Internal screen="bgv"><BgvPage /></Internal>} />
        <Route path="onboarding" element={<Internal screen="onboarding"><Phase1Placeholder screen="onboarding" stories="RCU-ONB-001..006" /></Internal>} />
        <Route path="vendors" element={<Internal screen="vendors"><Phase1Placeholder screen="vendors" stories="RCU-VEN-001..005" /></Internal>} />
        <Route path="reports" element={<Internal screen="reports"><Phase1Placeholder screen="reports" stories="RCU-RPT-001..006" /></Internal>} />
        <Route path="internal-careers" element={<Phase1Placeholder screen="internalCareers" stories="RCU-EMP-001..005" />} />
        <Route path="careers" element={<CareersPage />} />
        <Route path="team" element={<Allowed capability="team.invite"><TeamPage /></Allowed>} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  )
}
