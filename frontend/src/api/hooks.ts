// TanStack Query hooks over the ApiClient. Components use these, never the client directly
// for reads, so caching and invalidation stay consistent.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useSession } from '../auth/sessionContext'
import { features } from '../config/features'
import type { Role } from '../domain/types'
import { api } from './client'

export const keys = {
  branding: (workspace: string) => ['branding', workspace] as const,
  demoAccess: (workspace: string) => ['demoAccess', workspace] as const,
  invitation: (token: string) => ['invitation', token] as const,
  team: (tenantId: string) => ['team', tenantId] as const,
  invitations: (tenantId: string) => ['invitations', tenantId] as const,
  rules: ['rules'] as const,
  dashboard: ['dashboard'] as const,
  requisitions: ['requisitions'] as const,
  jd: (reqId: string) => ['jd', reqId] as const,
  pipeline: (reqId: string) => ['pipeline', reqId] as const,
  interview: (appId: string) => ['interview', appId] as const,
  candidate: (id: string) => ['candidate', id] as const,
  bgv: (appId: string) => ['bgv', appId] as const,
  offers: ['offers'] as const,
  approvals: (role: Role) => ['approvals', role] as const,
  notifications: (role: Role) => ['notifications', role] as const,
  emails: (role: Role) => ['emails', role] as const,
  postings: ['postings'] as const,
}

export const useWorkspaceBranding = (workspace: string | null) =>
  useQuery({
    queryKey: keys.branding(workspace ?? ''),
    queryFn: () => api.getWorkspaceBranding(workspace ?? ''),
    enabled: !!workspace,
    staleTime: Infinity,
  })

/** Demo personas for the workspace, or nothing when the demo switcher is off or the workspace has none. */
export const useDemoAccess = (workspace: string | null) =>
  useQuery({
    queryKey: keys.demoAccess(workspace ?? ''),
    queryFn: () => api.getDemoAccess(workspace ?? ''),
    enabled: features.demoPersonas && !!workspace,
    staleTime: Infinity,
  })

export const useInvitation = (token: string) =>
  useQuery({ queryKey: keys.invitation(token), queryFn: () => api.getInvitation(token) })

export function useTeam() {
  const { token, tenant } = useSession()
  return useQuery({ queryKey: keys.team(tenant.id), queryFn: () => api.listTeam(token) })
}

export function useInvitations() {
  const { token, tenant } = useSession()
  return useQuery({ queryKey: keys.invitations(tenant.id), queryFn: () => api.listInvitations(token) })
}

export const useRules = () => useQuery({ queryKey: keys.rules, queryFn: api.getRules, staleTime: Infinity })
export const useDashboard = () => useQuery({ queryKey: keys.dashboard, queryFn: api.getDashboard })
export const useRequisitions = () => useQuery({ queryKey: keys.requisitions, queryFn: api.listRequisitions })
export const useJobDescription = (reqId: string) =>
  useQuery({ queryKey: keys.jd(reqId), queryFn: () => api.getJobDescription(reqId) })
export const usePipeline = (reqId: string) =>
  useQuery({ queryKey: keys.pipeline(reqId), queryFn: () => api.listPipeline(reqId), enabled: !!reqId })
export const useInterview = (appId: string) =>
  useQuery({ queryKey: keys.interview(appId), queryFn: () => api.getInterview(appId) })
export const useBgvCase = (appId: string) => useQuery({ queryKey: keys.bgv(appId), queryFn: () => api.getBgvCase(appId) })
export const useOffers = () => useQuery({ queryKey: keys.offers, queryFn: api.listOffers })
export const useJobPostings = () => useQuery({ queryKey: keys.postings, queryFn: api.listJobPostings })

export function useApprovals() {
  const { user } = useSession()
  return useQuery({ queryKey: keys.approvals(user.role), queryFn: () => api.listApprovals(user.role) })
}

export function useNotifications() {
  const { user } = useSession()
  return useQuery({ queryKey: keys.notifications(user.role), queryFn: () => api.listNotifications(user.role) })
}

export function useEmails() {
  const { user } = useSession()
  return useQuery({ queryKey: keys.emails(user.role), queryFn: () => api.listEmails(user.role) })
}

/** Queries no business write can change. */
const STATIC_QUERIES = new Set(['rules', 'branding', 'demoAccess', 'invitation'])

/**
 * Mutations touch several entities at once (an MRF submit also creates an approval and a
 * notification), so after any write we refresh everything. Cheap with an in-memory mock; with
 * the real services this can be narrowed per mutation.
 */
export function useApiMutation<TArgs, TResult>(fn: (args: TArgs) => Promise<TResult>) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSettled: () => qc.invalidateQueries({ predicate: (q) => !STATIC_QUERIES.has(String(q.queryKey[0])) }),
  })
}
