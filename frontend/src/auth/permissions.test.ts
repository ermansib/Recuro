import { describe, expect, it } from 'vitest'
import type { WorkspaceRoleConfig } from '../domain/types'
import { can, canOpen } from './permissions'

const workspace: WorkspaceRoleConfig[] = [
  { key: 'hrta', name: 'HR-TA', permissions: ['pipeline.move'], pages: ['dashboard', 'pipeline'] },
]

describe('workspace roles from the admin portal', () => {
  it('uses the FRD matrix when the workspace has no roles configured', () => {
    expect(can('hrta', 'mrf.raise')).toBe(true)
    expect(can('mdceo', 'mrf.raise', null)).toBe(false)
    expect(canOpen('hrta', 'pipeline')).toBeNull()
  })

  it("lets a configured role's permissions decide, including taking one away", () => {
    expect(can('hrta', 'pipeline.move', workspace)).toBe(true)
    expect(can('hrta', 'mrf.raise', workspace)).toBe(false)
  })

  it('keeps the matrix for roles the workspace does not define', () => {
    expect(can('hrhead', 'mrf.approve', workspace)).toBe(true)
    expect(can('candidate', 'careers.apply', workspace)).toBe(true)
    expect(canOpen('hrhead', 'pipeline', workspace)).toBeNull()
  })

  it("opens only the configured role's pages", () => {
    expect(canOpen('hrta', 'pipeline', workspace)).toBe(true)
    expect(canOpen('hrta', 'offer', workspace)).toBe(false)
  })
})
