import { WORKSPACE_FORM, emptyWorkspaceForm, validateWorkspaceForm, withOrgType, workspaceFormLabels } from '@workspace-form'
import { ORG_TYPE_DEFAULTS } from './auth'
import type { OrgType } from './types'

// The create-workspace form is shared with the admin portal (admin/contracts/workspace-form).
describe('shared create-workspace form', () => {
  it('has a label for every org type and owner role', () => {
    for (const type of WORKSPACE_FORM.orgTypes) {
      expect(workspaceFormLabels.orgTypes[type]).toBeTruthy()
      expect(workspaceFormLabels.orgTypeHints[type]).toBeTruthy()
    }
    for (const role of WORKSPACE_FORM.ownerRoles) expect(workspaceFormLabels.roles[role]).toBeTruthy()
  })

  it('starts each org type with the owner role this portal provisions', () => {
    for (const type of Object.keys(ORG_TYPE_DEFAULTS) as OrgType[]) {
      expect(WORKSPACE_FORM.defaultOwnerRole[type]).toBe(ORG_TYPE_DEFAULTS[type].adminRole)
      expect(WORKSPACE_FORM.ownerRoles).toContain(WORKSPACE_FORM.defaultOwnerRole[type])
    }
    expect(withOrgType(emptyWorkspaceForm(), 'agency').adminRole).toBe('hrhead')
  })

  it('reports every missing field', () => {
    expect(Object.keys(validateWorkspaceForm(emptyWorkspaceForm())).sort()).toEqual(['adminName', 'email', 'orgName', 'password'])
    const ok = { ...emptyWorkspaceForm(), orgName: 'Acme', adminName: 'A. Doe', email: 'a@acme.example', password: 'Recuro@2026' }
    expect(validateWorkspaceForm(ok)).toEqual({})
    expect(validateWorkspaceForm({ ...ok, orgName: 'x'.repeat(WORKSPACE_FORM.orgNameMaxLength + 1) })).toHaveProperty('orgName')
  })
})
