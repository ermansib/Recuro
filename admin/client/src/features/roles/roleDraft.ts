import type { CreateRoleRequest, PermissionDefinition, WorkspaceRole } from '../../api/types'

/** The role form's state. `key` is only editable while the role is new. */
export interface RoleDraft {
  key: string
  name: string
  description: string
  reportsTo: string
  permissions: string[]
  pages: string[]
  requiresMfa: boolean
}

export const EMPTY_DRAFT: RoleDraft = {
  key: '',
  name: '',
  description: '',
  reportsTo: '',
  permissions: [],
  pages: [],
  requiresMfa: false,
}

export function draftFrom(role: WorkspaceRole): RoleDraft {
  return {
    key: role.key,
    name: role.name,
    description: role.description ?? '',
    reportsTo: role.reportsTo ?? '',
    permissions: [...role.permissions],
    pages: [...role.pages],
    requiresMfa: role.requiresMfa,
  }
}

export function toRequest(draft: RoleDraft): CreateRoleRequest {
  return {
    key: draft.key.trim().toLowerCase(),
    name: draft.name.trim(),
    description: draft.description.trim() || null,
    reportsTo: draft.reportsTo || null,
    permissions: draft.permissions,
    pages: draft.pages,
    requiresMfa: draft.requiresMfa,
  }
}

/** `Area Manager` → `area-manager`: a suggested key while the admin types a new role's name. */
export function keyFromName(name: string): string {
  return name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^[^a-z]+/, '')
    .replace(/-+$/, '')
    .slice(0, 40)
}

export function toggle(keys: string[], key: string, on: boolean): string[] {
  return on ? (keys.includes(key) ? keys : [...keys, key]) : keys.filter((k) => k !== key)
}

/** Permissions grouped by module, in catalogue order. */
export function byModule<T extends { module: string }>(items: T[]): [string, T[]][] {
  const groups = new Map<string, T[]>()
  for (const item of items) groups.set(item.module, [...(groups.get(item.module) ?? []), item])
  return [...groups.entries()]
}

export type { PermissionDefinition }
