import { Fragment, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useUpdateMember } from '../../api/hooks'
import type { RoleScope, WorkspaceMember, WorkspaceRole } from '../../api/types'
import { ErrorMessage } from '../../components/ui'
import { MEMBER_LIMITS } from './memberDraft'
import { ResetPasswordForm } from './ResetPasswordForm'
import { toggle } from './roleDraft'

const COLUMN_COUNT = 5

type Editing = { kind: 'none' } | { kind: 'roles' | 'password'; accountId: string }

interface MemberTableProps {
  scope: RoleScope
  members: WorkspaceMember[]
  roles: WorkspaceRole[]
  /** Called after a temporary password is set, with the person and the password to give them. */
  onPasswordSet: (member: WorkspaceMember, password: string) => void
}

/** People in the workspace and their roles. One person is edited at a time. */
export function MemberTable({ scope, members, roles, onPasswordSet }: MemberTableProps) {
  const { t } = useTranslation()
  const [editing, setEditing] = useState<Editing>({ kind: 'none' })
  const close = () => setEditing({ kind: 'none' })
  const isEditing = (kind: Editing['kind'], accountId: string) => editing.kind === kind && 'accountId' in editing && editing.accountId === accountId
  const names = new Map(roles.map((role) => [role.key, role.name]))

  if (members.length === 0) return <p className="muted">{t('roles.members.empty')}</p>

  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            <th scope="col">{t('roles.members.name')}</th>
            <th scope="col">{t('roles.members.email')}</th>
            <th scope="col">{t('roles.members.roles')}</th>
            <th scope="col">{t('roles.members.department')}</th>
            <th scope="col">
              <span className="sr-only">{t('roles.edit')}</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {members.map((member) =>
            isEditing('roles', member.accountId) ? (
              <MemberEditor key={member.accountId} scope={scope} member={member} roles={roles} onDone={close} />
            ) : (
              <Fragment key={member.accountId}>
                <tr>
                  <th scope="row">{member.name}</th>
                  <td>{member.email}</td>
                  <td>{member.roleKeys.map((key) => names.get(key) ?? key).join(', ') || '—'}</td>
                  <td>{member.department ?? '—'}</td>
                  <td>
                    <div className="actions" style={{ marginTop: 0 }}>
                      <button
                        type="button"
                        className="btn secondary small"
                        aria-label={t('roles.members.editFor', { name: member.name })}
                        onClick={() => setEditing({ kind: 'roles', accountId: member.accountId })}
                      >
                        {t('roles.edit')}
                      </button>
                      <button
                        type="button"
                        className="btn secondary small"
                        aria-label={t('roles.members.resetPasswordFor', { name: member.name })}
                        aria-expanded={isEditing('password', member.accountId)}
                        onClick={() => setEditing({ kind: 'password', accountId: member.accountId })}
                      >
                        {t('roles.members.resetPassword')}
                      </button>
                    </div>
                  </td>
                </tr>
                {isEditing('password', member.accountId) && (
                  <tr>
                    <td colSpan={COLUMN_COUNT}>
                      <ResetPasswordForm
                        scope={scope}
                        member={member}
                        onCancel={close}
                        onDone={(password) => {
                          close()
                          onPasswordSet(member, password)
                        }}
                      />
                    </td>
                  </tr>
                )}
              </Fragment>
            ),
          )}
        </tbody>
      </table>
    </div>
  )
}

function MemberEditor({ scope, member, roles, onDone }: { scope: RoleScope; member: WorkspaceMember; roles: WorkspaceRole[]; onDone: () => void }) {
  const { t } = useTranslation()
  const update = useUpdateMember(scope)
  const [roleKeys, setRoleKeys] = useState(member.roleKeys)
  const [department, setDepartment] = useState(member.department ?? '')
  // Switched-off roles stay listed only for people who already hold them, so they can be taken away.
  const choices = roles.filter((role) => role.isActive || member.roleKeys.includes(role.key))

  return (
    <tr>
      <th scope="row">{member.name}</th>
      <td>{member.email}</td>
      <td>
        <fieldset className="role-options compact">
          <legend className="sr-only">{t('roles.members.rolesFor', { name: member.name })}</legend>
          {choices.map((role) => (
            <label key={role.key} className="check">
              <input
                type="checkbox"
                checked={roleKeys.includes(role.key)}
                onChange={(event) => setRoleKeys(toggle(roleKeys, role.key, event.target.checked))}
              />
              {role.name}
            </label>
          ))}
        </fieldset>
        <ErrorMessage error={update.error} />
      </td>
      <td>
        <input
          className="input"
          aria-label={t('roles.members.departmentFor', { name: member.name })}
          maxLength={MEMBER_LIMITS.department}
          value={department}
          onChange={(event) => setDepartment(event.target.value)}
        />
      </td>
      <td>
        <div className="actions" style={{ marginTop: 0 }}>
          <button
            type="button"
            className="btn small"
            disabled={update.isPending || roleKeys.length === 0}
            onClick={() =>
              update.mutate(
                { accountId: member.accountId, request: { roleKeys, department: department.trim() || null } },
                { onSuccess: onDone },
              )
            }
          >
            {update.isPending ? t('common.saving') : t('common.save')}
          </button>
          <button type="button" className="btn secondary small" onClick={onDone}>
            {t('common.cancel')}
          </button>
        </div>
      </td>
    </tr>
  )
}
