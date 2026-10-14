import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { RoleCatalog, WorkspaceRole } from '../../api/types'
import { byModule, keyFromName, toggle, type RoleDraft } from './roleDraft'

interface RoleFormProps {
  /** The role being edited, or null for a new one. */
  role: WorkspaceRole | null
  initial: RoleDraft
  roles: WorkspaceRole[]
  catalog: RoleCatalog
  isSaving: boolean
  onSave: (draft: RoleDraft) => void
  onCancel: () => void
}

/** Create or edit a workspace role: name, reporting line, MFA, permissions and page access. */
export function RoleForm({ role, initial, roles, catalog, isSaving, onSave, onCancel }: RoleFormProps) {
  const { t } = useTranslation()
  const [draft, setDraft] = useState<RoleDraft>(initial)
  const [keyEdited, setKeyEdited] = useState(false)
  const isNew = role === null
  const change = (patch: Partial<RoleDraft>) => setDraft((current) => ({ ...current, ...patch }))
  const parents = roles.filter((r) => r.isActive && r.key !== draft.key)

  return (
    <form
      className="card"
      aria-labelledby="role-form-heading"
      onSubmit={(event) => {
        event.preventDefault()
        onSave(draft)
      }}
    >
      <h2 id="role-form-heading">{isNew ? t('roles.form.newTitle') : t('roles.form.editTitle', { role: role.name })}</h2>
      <div className="form-grid">
        <div className="field">
          <label htmlFor="role-name">{t('roles.form.name')}</label>
          <input
            id="role-name"
            required
            maxLength={60}
            value={draft.name}
            onChange={(event) =>
              change({ name: event.target.value, ...(isNew && !keyEdited ? { key: keyFromName(event.target.value) } : {}) })
            }
          />
        </div>
        <div className="field">
          <label htmlFor="role-key">{t('roles.form.key')}</label>
          <input
            id="role-key"
            required
            maxLength={40}
            pattern="[a-z][a-z0-9\-]*[a-z0-9]"
            disabled={!isNew}
            aria-describedby="role-key-help"
            value={draft.key}
            onChange={(event) => {
              setKeyEdited(true)
              change({ key: event.target.value })
            }}
          />
          <small id="role-key-help">{isNew ? t('roles.form.keyHelp') : t('roles.form.keyFixed')}</small>
        </div>
        <div className="field">
          <label htmlFor="role-reports-to">{t('roles.form.reportsTo')}</label>
          <select id="role-reports-to" className="input" value={draft.reportsTo} onChange={(event) => change({ reportsTo: event.target.value })}>
            <option value="">{t('roles.form.noParent')}</option>
            {parents.map((parent) => (
              <option key={parent.key} value={parent.key}>
                {parent.name}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label htmlFor="role-description">{t('roles.form.description')}</label>
          <input id="role-description" maxLength={300} value={draft.description} onChange={(event) => change({ description: event.target.value })} />
        </div>
      </div>
      <label className="check" style={{ marginTop: 14 }}>
        <input type="checkbox" checked={draft.requiresMfa} onChange={(event) => change({ requiresMfa: event.target.checked })} />
        {t('roles.form.requiresMfa')}
      </label>

      <fieldset className="role-options">
        <legend>{t('roles.form.permissions')}</legend>
        {byModule(catalog.permissions).map(([module, permissions]) => (
          <div key={module} className="role-group">
            <h3>{module}</h3>
            {permissions.map((permission) => (
              <label key={permission.key} className="check">
                <input
                  type="checkbox"
                  checked={draft.permissions.includes(permission.key)}
                  onChange={(event) => change({ permissions: toggle(draft.permissions, permission.key, event.target.checked) })}
                />
                {permission.label}
              </label>
            ))}
          </div>
        ))}
      </fieldset>

      <fieldset className="role-options">
        <legend>{t('roles.form.pages')}</legend>
        <div className="role-group">
          {catalog.pages.map((page) => (
            <label key={page.key} className="check">
              <input
                type="checkbox"
                checked={draft.pages.includes(page.key)}
                onChange={(event) => change({ pages: toggle(draft.pages, page.key, event.target.checked) })}
              />
              {page.code} · {page.title}
            </label>
          ))}
        </div>
      </fieldset>

      <div className="actions">
        <button type="submit" className="btn" disabled={isSaving}>
          {isSaving ? t('common.saving') : isNew ? t('roles.form.create') : t('common.save')}
        </button>
        <button type="button" className="btn secondary" onClick={onCancel}>
          {t('common.cancel')}
        </button>
      </div>
    </form>
  )
}
