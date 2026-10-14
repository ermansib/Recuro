// The signed-in workspace's own roles, configured in the admin portal (Roles & people) and served with the
// runtime config. Permissions and page access follow them; without them the FRD §3.2 matrix applies.
import { createContext, useContext } from 'react'
import type { WorkspaceRoleConfig } from '../domain/types'

/** Null until the workspace's runtime config has loaded, or when the admin portal can't be reached. */
export const WorkspaceRolesContext = createContext<WorkspaceRoleConfig[] | null>(null)

export function useWorkspaceRoles(): WorkspaceRoleConfig[] | null {
  return useContext(WorkspaceRolesContext)
}
