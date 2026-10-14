import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useApi } from './client'
import type {
  AssignTenantThemeRequest,
  CreateRoleRequest,
  CreateTenantRequest,
  RoleScope,
  UpdateMemberRequest,
  UpdateRoleRequest,
  UpdateScreenRequest,
  UpdateTenantRequest,
} from './types'

export const queryKeys = {
  personas: ['personas'] as const,
  tenants: ['platform', 'tenants'] as const,
  themes: ['platform', 'themes'] as const,
  branding: ['tenant', 'branding'] as const,
  screens: ['tenant', 'screens'] as const,
  screen: (key: string) => ['tenant', 'screens', key] as const,
  roleCatalog: ['roles', 'catalog'] as const,
  roles: (scope: RoleScope) => ['roles', scope ?? 'own', 'list'] as const,
  members: (scope: RoleScope) => ['roles', scope ?? 'own', 'members'] as const,
}

export function usePersonas() {
  const api = useApi()
  return useQuery({ queryKey: queryKeys.personas, queryFn: () => api.listPersonas() })
}

export function useTenants() {
  const api = useApi()
  return useQuery({ queryKey: queryKeys.tenants, queryFn: () => api.listTenants() })
}

export function useCreateTenant() {
  const api = useApi()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateTenantRequest) => api.createTenant(request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.tenants }),
  })
}

export function useUpdateTenant() {
  const api = useApi()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, request }: { id: string; request: UpdateTenantRequest }) => api.updateTenant(id, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.tenants }),
  })
}

export function useSetTenantSuspended() {
  const api = useApi()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, suspended }: { id: string; suspended: boolean }) => api.setTenantSuspended(id, suspended),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.tenants }),
  })
}

export function useAssignTenantTheme() {
  const api = useApi()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, request }: { id: string; request: AssignTenantThemeRequest }) => api.assignTenantTheme(id, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.tenants }),
  })
}

export function useThemes() {
  const api = useApi()
  return useQuery({ queryKey: queryKeys.themes, queryFn: () => api.listThemes() })
}

export function useSetThemePublished() {
  const api = useApi()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ key, isPublished }: { key: string; isPublished: boolean }) => api.setThemePublished(key, isPublished),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.themes }),
  })
}

export function useBranding() {
  const api = useApi()
  return useQuery({ queryKey: queryKeys.branding, queryFn: () => api.getBranding() })
}

export function useScreens() {
  const api = useApi()
  return useQuery({ queryKey: queryKeys.screens, queryFn: () => api.listScreens() })
}

export function useScreen(key: string) {
  const api = useApi()
  return useQuery({ queryKey: queryKeys.screen(key), queryFn: () => api.getScreen(key) })
}

export function useUpdateScreen(key: string) {
  const api = useApi()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: UpdateScreenRequest) => api.updateScreen(key, request),
    onSuccess: (screen) => {
      queryClient.setQueryData(queryKeys.screen(key), screen)
      return queryClient.invalidateQueries({ queryKey: queryKeys.screens, exact: true })
    },
  })
}

export function useRoleCatalog(scope: RoleScope) {
  const api = useApi()
  return useQuery({ queryKey: queryKeys.roleCatalog, queryFn: () => api.getRoleCatalog(scope) })
}

export function useRoles(scope: RoleScope) {
  const api = useApi()
  return useQuery({ queryKey: queryKeys.roles(scope), queryFn: () => api.listRoles(scope) })
}

export function useMembers(scope: RoleScope) {
  const api = useApi()
  return useQuery({ queryKey: queryKeys.members(scope), queryFn: () => api.listMembers(scope) })
}

/** Role changes can change member counts and the reporting tree, so they refresh the whole workspace. */
function useRolesMutation<TVariables, TResult>(scope: RoleScope, mutationFn: (variables: TVariables) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn,
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.roles(scope) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.members(scope) }),
      ]),
  })
}

export function useCreateRole(scope: RoleScope) {
  const api = useApi()
  return useRolesMutation(scope, (request: CreateRoleRequest) => api.createRole(scope, request))
}

export function useUpdateRole(scope: RoleScope) {
  const api = useApi()
  return useRolesMutation(scope, ({ key, request }: { key: string; request: UpdateRoleRequest }) => api.updateRole(scope, key, request))
}

export function useSetRoleActive(scope: RoleScope) {
  const api = useApi()
  return useRolesMutation(scope, ({ key, isActive }: { key: string; isActive: boolean }) => api.setRoleActive(scope, key, isActive))
}

export function useUpdateMember(scope: RoleScope) {
  const api = useApi()
  return useRolesMutation(scope, ({ accountId, request }: { accountId: string; request: UpdateMemberRequest }) =>
    api.updateMember(scope, accountId, request),
  )
}
