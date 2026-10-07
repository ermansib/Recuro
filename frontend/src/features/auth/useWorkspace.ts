import { useSearchParams } from 'react-router-dom'
import { useAuth } from '../../auth/sessionContext'

/**
 * Workspace for a signed-out page: `?workspace=` wins, then the one used last on this device.
 * An empty `?workspace=` means "let me pick another one".
 */
export function useWorkspaceParam(): string | null {
  const [params] = useSearchParams()
  const { lastWorkspace } = useAuth()
  const fromUrl = params.get('workspace')
  if (fromUrl !== null) return fromUrl.trim().toLowerCase() || null
  return lastWorkspace
}

export const withWorkspace = (path: string, workspace: string | null | undefined) =>
  workspace ? `${path}?workspace=${encodeURIComponent(workspace)}` : path
