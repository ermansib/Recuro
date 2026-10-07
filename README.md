# Recuro

Recuro is a white-label recruitment portal for in-house HR teams and staffing agencies, growing into a
recruitment CRM. The React + TypeScript frontend is built first, on static JSON mocks; .NET Core
microservices come later. See [CLAUDE.md](CLAUDE.md) for the architecture and conventions.

## Run the frontend

```bash
cd frontend
npm install
npm run dev        # http://localhost:5173
npm test           # unit and component tests
npm run build      # typecheck + production build
```

Use the persona menu at the top right to switch between HR-TA, HR Head, MD/CEO, Employee and
Candidate. Data lives in memory and resets on reload.
