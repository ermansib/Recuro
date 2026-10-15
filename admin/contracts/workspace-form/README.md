# Create-workspace form

One definition of the form that opens a workspace, used in two places:

- the main portal's sign-up page (`frontend/src/features/auth/SignUpPage.tsx`), and
- the admin portal's platform console, Workspaces → Add a workspace (`admin/client/src/features/tenants/CreateTenantForm.tsx`).

| File | What it holds | Who reads it |
|---|---|---|
| `definition.json` | Fields and order, org types, owner roles and the default role per org type, limits | `index.ts`, and the admin API's `WorkspaceFormContractTests` |
| `en.json` | English labels, hints and errors (i18n key `workspaceForm`) | both React apps |
| `index.ts` | Types, defaults, password rules, slug rule and `validateWorkspaceForm` | both React apps, through the `@workspace-form` alias |

To change the form, change it here and in the admin API (`Recuro.Admin.Domain.Tenants.WorkspaceForm`,
`OpenWorkspaceHandler`); the contract tests and both apps' tests fail until all three agree. Keep `index.ts`
free of package imports: both apps compile it from this folder.
