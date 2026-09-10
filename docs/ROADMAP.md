# Product Scope & Milestone Roadmap — Billing & Subscription Platform

See `docs/research/M0-Billing-Platform-Research-Brief.md` for the full domain research and UX reference this roadmap is built from, and `docs/ARCHITECTURE.md` for the technical decisions.

## MVP definition

In scope for MVP (M1–M5): organizations, customers, product/price catalog (flat, per-seat, tiered, metered), subscription lifecycle with proration, invoicing, a deterministic simulated payment gateway with dunning, webhook delivery with HMAC signing and an event log, and the virtual-clock "time travel" simulation console that makes all of the above demoable without waiting on real calendar time.

Out of scope / cut per scope control: real payment processing, multi-currency FX conversion, tax-jurisdiction engines beyond a flat simulated rate, a real customer-facing storefront/checkout, multi-region deployment, microservices.

M6 (dashboard analytics + hardening) is a stretch milestone — build it only once M1–M5 are solid; don't let it crowd out core billing correctness.

## Milestones

Each milestone follows: plan → delegate → implement → test → review (Codex QA / Antigravity) → fix BLOCKER/IMPORTANT → commit → push → next milestone. No stopping to ask after each one.

### M1 — Foundations — COMPLETE
- **Backend:** solution scaffold (per-module Domain/Application/Infrastructure/Api projects), `Directory.Build.props`, `ArchitectureTests` project wired in from day one, Identity + Organizations modules (auth, tenant boundary), `IVirtualClock` service, Serilog + OpenTelemetry wiring, health checks, Docker Compose (Postgres), base EF Core migrations.
- **Frontend:** Vite+React+TS scaffold, Tailwind v4 tokens (zinc/slate neutral, semantic status colors per the research brief §6.2), light/dark theme system, app shell (sidebar, top nav, persistent Simulation Bar placeholder), auth screens, API client (fetch wrapper + ProblemDetails parsing).
- **Exit criteria:** empty-but-running app, authenticated, deployed locally via Docker Compose, architecture tests green, CI pipeline running lint/build/test on push.

### M2 — Customers & Catalog — COMPLETE
- **Backend:** Customers module (CRUD, credit balance, delinquency flag), Catalog module (Products, Prices — all four pricing models), tenant-isolation tests.
- **Frontend:** Customers list/detail (slide-over sheet), Products/Prices pages with the tiered/per-seat price builder.
- **Hardening from QA (Antigravity + Codex QA independent passes):** balance/delinquency are immutable via the public API after creation; cross-tenant customer lookups return a uniform 404 (no existence oracle); tiered prices must be contiguous (gaps rejected, not just overlaps); customer email is unique per organization (DB constraint + 409); `MeteredAggregation` validated against defined enum values; backend email validation tightened to match the frontend; frontend surfaces unmapped server validation errors in a form-level alert instead of dropping them; table rows use real interactive elements for full keyboard support.
- **Deferred, consciously (OPTIONAL, not this milestone):** optimistic concurrency on Customer/Product/Price updates — no financial mutation happens until M3/M4, revisit then; real-time organization-membership revocation (currently token-lifetime bound); a hard ceiling on price-builder decimal input to avoid float/safe-integer edge cases before real money math starts consuming it.

### M3 — Subscriptions — COMPLETE
- **Backend:** Subscription state machine (trialing/active/past_due/unpaid/canceled/paused), proration engine (integer-cents, BigInteger-safe), seat adjustments, optimistic concurrency (real EF concurrency token, proven by a 5-way concurrent HTTP race test). `PastDue`/`Unpaid`/`Recover` transitions are modeled but not yet reachable via any endpoint — reserved for M4's dunning engine. `preview-proration`/`apply-change` compute and return proration but don't yet persist an invoice line item (no Billing module until M4); that seam is commented in the code for M4 to pick up.
- **Frontend:** Subscriptions list/detail, upgrade/downgrade flow with the "Preview Proration" modal (research brief §3, Reference Point 3). Cancellation is immediate-only this milestone (no `cancel_at_period_end` — needs M4/M5 renewal-cron infrastructure).
- **Hardening from QA (Antigravity + Codex QA independent passes):** every subscription mutation (create/apply-change/cancel/pause/resume) requires an `Idempotency-Key`, persisted with a DB-unique constraint and atomic replay — same key + same body replays the stored response, same key + different body is 409; this is the project's first real implementation of the idempotency pattern, meant to be reused by M4/M5's webhooks and payments. Trialing subscriptions are never charged when changing plan/seats (proration zeroes out, price/seat still change) until `trial_end`. Pause is only valid from `Active`; Resume only from `Paused`; plan/seat changes are rejected while `Paused` (must Resume first). Downgrading from a seat-based price (PerSeat/Tiered) to a non-seat price (Flat/Metered) now correctly clears `SeatCount` instead of getting stuck rejecting the request.
- **Deferred, consciously (OPTIONAL, not this milestone):** the proration *preview* for a no-op plan change (same price/seat resubmitted) shows offsetting non-zero credit/charge lines that net to zero — cosmetic only, `apply-change` already correctly no-ops without a version bump; whether an organization/customer may hold prices in more than one currency is an open policy question, cross-cutting beyond Subscriptions, to resolve alongside M4's invoicing design.

### M4 — Invoicing & Payments
- **Backend:** Invoice generation (draft→open→paid/void/uncollectible), line items incl. proration/credit-note items, deterministic test-card payment simulator, dunning retry state machine driven by `IVirtualClock`.
- **Frontend:** Invoices list + split-pane invoice detail (paper-style left pane, actions right pane), test-card selector, dunning cockpit.

### M5 — Webhooks & Simulation Console
- **Backend:** Outbox-based webhook delivery, HMAC-SHA256 signing, delivery log, Inbox-based idempotent event ingestion, full virtual-clock time-travel API (+1/+7/+30 days, advance-to-next-anchor, trigger renewal cron, process dunning sweep, demo dataset seeder).
- **Frontend:** Developers/Webhooks pages, expandable JSON event timeline, the fully wired Simulation Bar.
- **This is the milestone with the highest-value test scenarios** — see Test Strategy below. Codex QA should review here especially hard.

### M6 — Analytics & Production Polish (stretch)
- **Backend:** MRR/ARR/churn/waterfall read models.
- **Frontend:** Executive dashboard.
- Full security review pass, docs pass, deploy to Render, production smoke test, portfolio freeze.

## Test strategy — required scenarios

These map directly to the domain invariants in `docs/ARCHITECTURE.md` and must exist as real integration tests, not just asserted in code review:

1. **Duplicate webhook delivery → effect occurs once** (Inbox unique-constraint test).
2. **Two concurrent renewal-cron workers → no duplicate invoice** (optimistic concurrency / row-lock test).
3. **Duplicate payment request (same idempotency key) → no duplicate charge/effect.**
4. **Cross-tenant access attempt → denied** (IDOR test per module with data).
5. **Failed payment → dunning retry policy fires on the correct virtual-clock schedule.**
6. **Out-of-order event ingestion → safe, no corrupted state.**

## Deployment strategy

Docker Compose for local dev (Postgres + api + web, matching Fleet/CMMS/HelpDesk/Order&Inventory conventions). Render.com (`render.yaml`) as the deploy target once M1–M5 are stable — same platform as the rest of the portfolio, keeps deployment familiar and low-effort so the project can focus on domain/backend depth.

## Worker ownership

See project root `MEMORY.md` → Maestri worker model. Backend milestones default to Codex — Backend; Claude — Backend Fallback takes over immediately on usage-limit/stall and hands back at the next safe checkpoint. Frontend is Claude — Frontend / UI throughout, informed by Antigravity research before each new screen. Codex QA reviews incrementally per milestone, independently.
