// TanStack Query hooks over the ApiClient. Components use these, never the client directly
// for reads, so caching and invalidation stay consistent.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useSession } from '../auth/sessionContext'
import type { Role } from '../domain/types'
import { api } from './client'

export const keys = {
  users: ['users'] as const,
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

/**
 * Mutations touch several entities at once (an MRF submit also creates an approval and a
 * notification), so after any write we refresh everything. Cheap with an in-memory mock; with
 * the real services this can be narrowed per mutation.
 */
export function useApiMutation<TArgs, TResult>(fn: (args: TArgs) => Promise<TResult>) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSettled: () => qc.invalidateQueries({ predicate: (q) => q.queryKey[0] !== 'rules' }),
  })
}
