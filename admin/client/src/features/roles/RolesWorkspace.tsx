import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useCreateRole, useMembers, useRoleCatalog, useRoles, useSetRoleActive, useUpdateRole } from '../../api/hooks'
import type { RoleCatalog, RoleScope, WorkspaceMember, WorkspaceRole } from '../../api/types'
import { ErrorMessage, QueryState, StatusMessage } from '../../components/ui'
import { AddMemberForm } from './AddMemberForm'
import { MemberTable } from './MemberTable'
import { RoleForm } from './RoleForm'
import { draftFrom, EMPTY_DRAFT, toRequest, type RoleDraft } from './roleDraft'

type Editing = { kind: 'none' } | { kind: 'new' } | { kind: 'edit'; role: WorkspaceRole }

/**
 * One workspace's roles and people. Used by the tenant admin for their own workspace and by the platform
 * console for any workspace (scope = tenant id).
 */
export function RolesWorkspace({ scope }: { scope: RoleScope }) {
  const roles = useRoles(scope)
  const catalog = useRoleCatalog(scope)
  const members = useMembers(scope)

  return (
    <QueryState isLoading={roles.isLoading || catalog.isLoading} error={roles.error ?? catalog.error}>
      {roles.data && catalog.data && (
        <>
          <RoleSection scope={scope} roles={roles.data} catalog={catalog.data} />
          <PeopleSection scope={scope} roles={roles.data} members={members.data} isLoading={members.isLoading} error={members.error} />
        </>
      )}
    </QueryState>
  )
}

interface PeopleSectionProps {
  scope: RoleScope
  roles: WorkspaceRole[]
  members: WorkspaceMember[] | undefined
  isLoading: boolean
  error: Error | null
}

/** Everyone in the workspace, with adding a person and setting a temporary password (email isn't set up yet). */
function PeopleSection({ scope, roles, members, isLoading, error }: PeopleSectionProps) {
  const { t } = useTranslation()
  const [adding, setAdding] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)

  const announce = (message: string) => {
    setAdding(false)
    setNotice(message)
  }

  return (
    <section className="card" aria-labelledby="members-heading">
      <h2 id="members-heading">{t('roles.members.title')}</h2>
      <p className="muted">{t('roles.members.subtitle')}</p>
      <StatusMessage message={notice} />
      <QueryState isLoading={isLoading} error={error}>
        {members && (
          <MemberTable
            scope={scope}
            members={members}
            roles={roles}
            onPasswordSet={(member, password) => announce(t('roles.members.passwordSet', { name: member.name, password }))}
          />
        )}
      </QueryState>
      {adding ? (
        <AddMemberForm
          scope={scope}
          roles={roles}
          onCancel={() => setAdding(false)}
          onAdded={(member) => announce(t('roles.members.added', { name: member.name, email: member.email }))}
        />
      ) : (
        <div className="actions">
          <button
            type="button"
            className="btn"
            onClick={() => {
              setNotice(null)
              setAdding(true)
            }}
          >
            {t('roles.members.add')}
          </button>
        </div>
      )}
    </section>
  )
}

function RoleSection({ scope, roles, catalog }: { scope: RoleScope; roles: WorkspaceRole[]; catalog: RoleCatalog }) {
  const { t } = useTranslation()
  const [editing, setEditing] = useState<Editing>({ kind: 'none' })
  const [saved, setSaved] = useState<string | null>(null)
  const create = useCreateRole(scope)
  const update = useUpdateRole(scope)
  const setActive = useSetRoleActive(scope)
  const names = new Map(roles.map((role) => [role.key, role.name]))

  const done = (name: string) => {
    setEditing({ kind: 'none' })
    setSaved(t('roles.savedToast', { role: name }))
  }

  const save = (draft: RoleDraft) => {
    const request = toRequest(draft)
    if (editing.kind === 'edit') {
      const { key: _key, ...changes } = request
      update.mutate({ key: editing.role.key, request: changes }, { onSuccess: (role) => done(role.name) })
    } else {
      create.mutate(request, { onSuccess: (role) => done(role.name) })
    }
  }

  return (
    <>
      <section className="card" aria-labelledby="roles-heading">
        <h2 id="roles-heading">{t('roles.listTitle')}</h2>
        <ErrorMessage error={setActive.error} />
        <StatusMessage message={saved} />
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">{t('roles.columns.role')}</th>
                <th scope="col">{t('roles.columns.key')}</th>
                <th scope="col">{t('roles.columns.reportsTo')}</th>
                <th scope="col">{t('roles.columns.access')}</th>
                <th scope="col">{t('roles.columns.members')}</th>
                <th scope="col">{t('roles.columns.status')}</th>
                <th scope="col">{t('roles.columns.actions')}</th>
              </tr>
            </thead>
            <tbody>
              {roles.map((role) => (
                <tr key={role.key}>
                  <th scope="row">
                    {role.name}
                    {role.requiresMfa && <span className="badge ok"> {t('roles.mfa')}</span>}
                    {role.description && (
                      <div className="muted" style={{ fontWeight: 400, fontSize: 12 }}>
                        {role.description}
                      </div>
                    )}
                  </th>
                  <td className="hex">{role.key}</td>
                  <td>{role.reportsTo ? (names.get(role.reportsTo) ?? role.reportsTo) : '—'}</td>
                  <td>{t('roles.accessSummary', { permissions: role.permissions.length, pages: role.pages.length })}</td>
                  <td>{role.memberCount}</td>
                  <td>
                    <span className={`badge ${role.isActive ? 'ok' : 'off'}`}>{role.isActive ? t('roles.active') : t('roles.inactive')}</span>
                  </td>
                  <td>
                    <div className="actions" style={{ marginTop: 0 }}>
                      <button
                        type="button"
                        className="btn secondary small"
                        aria-label={t('roles.editRole', { role: role.name })}
                        onClick={() => setEditing({ kind: 'edit', role })}
                      >
                        {t('roles.edit')}
                      </button>
                      <button
                        type="button"
                        className="btn secondary small"
                        disabled={setActive.isPending}
                        aria-label={t(role.isActive ? 'roles.deactivateRole' : 'roles.activateRole', { role: role.name })}
                        onClick={() => setActive.mutate({ key: role.key, isActive: !role.isActive })}
                      >
                        {role.isActive ? t('roles.deactivate') : t('roles.activate')}
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {editing.kind === 'none' && (
          <div className="actions">
            <button type="button" className="btn" onClick={() => setEditing({ kind: 'new' })}>
              {t('roles.add')}
            </button>
          </div>
        )}
      </section>

      {editing.kind !== 'none' && (
        <>
          <ErrorMessage error={create.error ?? update.error} />
          <RoleForm
            key={editing.kind === 'edit' ? editing.role.key : 'new'}
            role={editing.kind === 'edit' ? editing.role : null}
            initial={editing.kind === 'edit' ? draftFrom(editing.role) : EMPTY_DRAFT}
            roles={roles}
            catalog={catalog}
            isSaving={create.isPending || update.isPending}
            onSave={save}
            onCancel={() => setEditing({ kind: 'none' })}
          />
        </>
      )}
    </>
  )
}
