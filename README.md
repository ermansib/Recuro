# Recuro

Recuro is a white-label recruitment portal for in-house HR teams and staffing agencies, growing into a
recruitment CRM. The React + TypeScript frontend is built first, on static JSON mocks; .NET Core
microservices come later. See [CLAUDE.md](CLAUDE.md) for the architecture and conventions.

The admin portal (platform console and tenant admin, .NET 10 + React) lives in [`admin/`](admin/README.md).

## Run the frontend

```bash
cd frontend
npm install
npm run dev        # http://localhost:5173
npm test           # unit and component tests
npm run build      # typecheck + production build
```

Sign in at `/signin`. Choose **Open the demo workspace** (Aurora Housing Finance), then sign in with
any demo account and the password `Recuro@2026`, or click a persona under **Demo personas**. HR Head
and MD/CEO get a second-factor code, shown on screen in the demo. You can also create your own
workspace at `/signup` (small business, recruitment agency or enterprise), register as a candidate
from a workspace's sign-in page, reset a password, and invite colleagues from **Team & invitations**
in the account menu.

The demo personas (and the persona switcher in the account menu) appear in `npm run dev` only; set
`VITE_DEMO_PERSONAS=true` to keep them in a production build. Accounts and workspaces you create are
kept in the browser's localStorage; hiring data lives in memory and resets on reload.
