# Billing & Subscription Platform

A portfolio SaaS billing simulator — the kind of recurring-billing engine
behind products like Stripe Billing, Chargebee, or Paddle — built to
demonstrate serious backend engineering (idempotency, outbox-pattern
webhooks, cross-module transactional atomicity, optimistic concurrency,
structured observability) alongside a polished SaaS frontend.

**Zero real payments, ever.** Card numbers map to outcomes through a fixed,
deterministic test-card table; there is no real payment processor
integration and no real card data is ever stored or transmitted.

## What it does

- **Organizations & customers** — multi-tenant boundary, customer records
  with credit balance and delinquency status.
- **Product/price catalog** — flat, per-seat, tiered, and metered pricing
  models.
- **Subscriptions** — full lifecycle (trialing → active → past due → unpaid
  → canceled, plus pause/resume), proration on upgrades/downgrades with a
  preview step, optimistic concurrency.
- **Invoicing & payments** — draft → open → paid/void/uncollectible invoice
  lifecycle, a deterministic simulated payment gateway, and a dunning retry
  engine on a day-3/7/14 schedule.
- **Webhooks** — outbox-based delivery with HMAC-SHA256 signing, delivery
  leases, and a full event log.
- **Simulation Console** — a virtual clock (`IVirtualClock`) that replaces
  system time everywhere billing logic reads time, so renewals, dunning
  retries, and webhook replay windows can be fast-forwarded on demand
  instead of waiting on real calendar time.
- **Executive dashboard & reporting** — MRR/ARR run-rate, a New/Expansion/
  Contraction/Churn waterfall, and an at-risk-revenue alert, computed from a
  tenant-scoped, append-only domain event log.

See `docs/ROADMAP.md` for the full milestone history and `docs/ARCHITECTURE.md`
for the technical decisions and domain invariants behind all of the above.

## Stack

| Layer | Choices |
|---|---|
| Backend | C#, .NET 10, ASP.NET Core minimal APIs, EF Core, PostgreSQL |
| Frontend | Vite, React 19, TypeScript, Tailwind CSS v4, TanStack Query |
| Observability | Serilog (structured logs) + OpenTelemetry (tracing/metrics) |
| Testing | xUnit + Testcontainers (real PostgreSQL), Vitest + Testing Library |
| Deployment | Docker Compose (local), Render.com (hosted) |

Architecture is a **modular monolith**, not microservices — one schema and
one `DbContext` per module, module boundaries enforced at build time via
`NetArchTest`, cross-module calls only through another module's
Application-layer contracts.

## Running it locally

```bash
# from the repo root
cp .env.example .env   # then fill in POSTGRES_PASSWORD and JWT_SIGNING_KEY
docker compose up --build
```

`.env` needs:

```bash
POSTGRES_PASSWORD=<a local dev password>
JWT_SIGNING_KEY=<32+ bytes, e.g. `openssl rand -base64 48`>
```

- API: `http://localhost:8080` (`/health/live`, `/health/ready`, OpenAPI at
  `/openapi/v1.json` in development)
- Web: `http://localhost:5173`

For backend-only or frontend-only development (including running each
module's own test suite), see `backend/README.md` and `frontend/README.md`.

## Known limitations

These are deliberate, documented scope decisions for a single-operator
portfolio demo — not oversights:

- `IVirtualClock` is a process-global singleton, not per-organization. One
  organization's time-travel action advances every organization's renewal
  eligibility, dunning timing, and webhook HMAC replay window. Correct
  behavior for concurrent multi-tenant production traffic would need an
  org-scoped clock context; out of scope for a demo built around one
  operator driving the simulation.
- No real payment processor, no multi-currency FX conversion, no
  tax-jurisdiction engine beyond a flat simulated rate, no customer-facing
  storefront/checkout.
- The demo-dataset seeder is not concurrency-safe (two simultaneous "Seed
  Demo Dataset" clicks can create duplicate catalog rows) — internal demo
  tooling, not a financial-correctness path.
- A session is pinned to its earliest organization membership; there's no
  tenant-switching endpoint for a second org created from the same account.
  Single-org-per-session is the intended shape for a single-operator demo.

## Live demo

- **App:** https://frontend-fawn-beta-42.vercel.app
- **API:** https://billing-platform-api-57i7.onrender.com (`/health/ready`, `/health/live`)

Deployed on Render (API, Docker, free tier — `render.yaml` at the repo root)
+ Neon (Postgres, serverless) + Vercel (frontend static site). The API's
`Cors__AllowedOrigins__0` and `ConnectionStrings__BillingPlatform` are set
directly on the Render service rather than synced from `render.yaml`, since
the database is Neon-hosted, not a Render-managed Postgres instance.

Free-tier services sleep after 15 minutes idle; the first request after a
period of inactivity may take 30-60s to wake the API (Render) and, less
commonly, the frontend (Vercel serves it from its CDN and rarely cold-starts,
but the API behind it can). A sleeping API also loses its in-memory virtual
clock offset and background webhook dispatcher state on wake — both resume
correctly, just reset to their defaults. Consider Render's Starter tier
(~$14/mo total) to remove this for an active demo period.

**Operational notes, learned the hard way wiring this up:**
- Changing a Render service's environment variables via the API/dashboard does **not** take effect on `restart` — only a fresh `deploys create` (redeploy) re-injects the updated environment into the container.
- The Vercel project's Root Directory must be set explicitly (`frontend`) for its GitHub auto-deploy to work — without it, Vercel clones the whole monorepo but never `cd`s into `frontend/` first, so its dependency-install step silently never runs and every auto-deploy fails (`vite: command not found`). `VITE_API_URL`/`VITE_API_MOCKING` are set as persistent Vercel project environment variables for the same reason — a one-off CLI flag doesn't help a build Vercel triggers itself.
- Vercel's static hosting needs an explicit SPA fallback (`frontend/vercel.json`); without it, any direct navigation to a client-side route (a bookmark, a refresh, a shared link) 404s. The Docker/nginx deployment already has the equivalent (`nginx.conf`'s `try_files ... /index.html`).

## Status

M1–M6 (all planned milestones, including the M6 analytics stretch goal) are
complete and CI-green, including a full security review pass and a
production smoke test that boots the real Docker Compose stack and exercises
an authenticated flow end-to-end in CI. **Deployed and verified live** (see
Live demo above) — register/login/authenticated-call and CORS all confirmed
working against the real production stack, not just CI. See
`docs/ROADMAP.md` for full milestone history.
