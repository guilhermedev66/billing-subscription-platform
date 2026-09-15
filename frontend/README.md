# Billing Platform frontend

Vite + React 19 + TypeScript SaaS frontend for the billing simulator:
organizations, customers, product/price catalog, subscription lifecycle with
proration previews, invoices and a dunning cockpit, webhook management, an
executive analytics dashboard, and a persistent Simulation Bar that drives
the backend's virtual-clock "time travel" console.

## Stack

- Vite, React 19, TypeScript
- Tailwind CSS v4 (CSS-first `@theme`, semantic status tokens, light/dark)
- TanStack Query for server state
- react-hook-form + zod for forms/validation
- react-router-dom v7
- Vitest + Testing Library (colocated tests)
- MSW for local mocking when the real API isn't running

## Setup

```bash
npm install
cp .env.example .env.local   # adjust VITE_API_URL / VITE_API_MOCKING
npm run dev
```

By default `VITE_API_MOCKING=enabled` serves data from MSW handlers
(`src/mocks/handlers.ts`) so the UI runs standalone. Set it to `disabled`
(and point `VITE_API_URL` at a running backend, e.g.
`http://localhost:8080/api`) to hit the real API — see `../backend/README.md`
or `docker compose up --build` from the repo root to run both together.

## Scripts

```bash
npm run dev        # dev server with HMR
npm run build       # typecheck + production build
npm run lint         # oxlint
npm run test         # vitest run
npm run test:watch   # vitest watch mode
npm run preview      # preview a production build locally
```

## Structure

Pages are organized per domain area (Customers, Catalog, Subscriptions,
Invoices, Payments, Developers/Webhooks, Dashboard), sharing a common app
shell (sidebar, top nav, persistent Simulation Bar) and API client (fetch
wrapper + `ProblemDetails` parsing). See `../docs/ARCHITECTURE.md` for the
backend module map this frontend consumes, and `../docs/ROADMAP.md` for
what's implemented per milestone.
