# Architecture Decision — Billing & Subscription Platform

## What this is

A portfolio SaaS billing simulator: realistic recurring-billing domain and workflows (Stripe/Chargebee/Paddle/Lemon Squeezy-style), zero real money, zero real card data. Goal is to demonstrate serious backend engineering (webhooks, idempotency, background jobs, concurrency, observability) plus a polished SaaS frontend — not to become a real payment processor, an accounting ERP, or a microservices playground.

## Why modular monolith

Matches the author's most mature prior projects (Fleet & Delivery Management System, CMMS) and the explicit project brief. Microservices would add operational overhead with no payoff for a portfolio project of this scope. Module boundaries are enforced in-process (NetArchTest) rather than via network calls.

## Stack

- **Backend:** C#, .NET 10, ASP.NET Core minimal APIs, EF Core, PostgreSQL.
- **Frontend:** Vite + React 19 + TypeScript, Tailwind CSS v4 (CSS-first `@theme`), TanStack Query, react-hook-form + zod, react-router-dom v7.
- **No Redis at MVP.** No established use case yet (idempotency and dunning state fit fine in Postgres via unique constraints/row locks). Revisit only if a concrete need appears (e.g. rate limiting at scale) — don't force it in for the keyword.
- **Docker Compose** for local dev (Postgres + api + web), following the same multi-stage Dockerfile pattern as Fleet/CMMS/HelpDesk/Order&Inventory. Render.com (`render.yaml`) as the deploy target, matching the established portfolio convention.

## Module map (backend)

One schema + one `DbContext` per module (Fleet/CMMS pattern), snake_case columns (CMMS pattern — idiomatic Postgres), cross-module communication only through another module's Application-layer contracts (no cross-schema FKs):

| Module | Owns |
|---|---|
| **Identity** | Users, auth, JWT issuance (ASP.NET Core Identity underneath, hand-rolled `ITokenService` on top — HelpDesk/Order&Inventory pattern). Fail loud at startup if signing key missing/weak. |
| **Organizations** | Tenant boundary (`org_id`), org settings (currency, invoice numbering, webhook secrets, API keys), simulation-mode flag. |
| **Customers** | Customer entity, credit balance, delinquency status, payment methods, self-serve portal read model. |
| **Catalog** | Products, Prices (flat / per-seat / tiered / metered), trial config. |
| **Subscriptions** | Subscription state machine, proration engine, seat adjustments, cycle anchors. |
| **Billing** | Invoices, line items, credit notes. Finalized invoices are immutable — mutations produce new proration/credit line items, never in-place edits. |
| **Payments** | Simulated deterministic payment gateway (fixed test-card table → outcomes), payment attempts, dunning retry state machine. |
| **Webhooks** | Outbound webhook endpoint registration, HMAC-SHA256 signing, delivery log, retry. |
| **SimulationClock** | Cross-cutting `IVirtualClock` — every expiry/renewal/proration check reads this, never `DateTime.UtcNow`, so billing time can be fast-forwarded deterministically for demos and tests. |
| **Reporting** | Event log (audit trail), MRR/ARR/churn analytics read models. |

`Domain` → `Application` → `Infrastructure` → `Api` per module, boundaries enforced by a `*.ArchitectureTests` project (NetArchTest), same as Fleet.

## Domain invariants (non-negotiable)

1. **Money is integer cents.** No `float`/`double` anywhere in the money path.
2. **Idempotency keys** required on every payment- and subscription-mutating endpoint, and on inbound simulated webhook/event ingestion — a duplicate request or duplicate delivery must never duplicate a billing effect. Implemented via a DB unique constraint (Inbox pattern, CMMS/Fleet-style), not just an in-memory cache.
3. **Outbox pattern** for anything that must reach the outside world (webhook delivery): write the domain change and the `Event` row in the same transaction; a background worker polls, signs, and delivers — never fire-and-forget HTTP calls inline with the request.
4. **Tenant isolation**: every query is scoped by `org_id`; no cross-org reads/writes (tested explicitly — IDOR is a BLOCKER-class QA finding).
5. **Finalized invoices are immutable.** Corrections are new line items / credit notes referencing the original, never row edits.
6. **Optimistic concurrency** on `subscriptions` (a version column) to prevent races between the renewal cron and a manual customer-initiated upgrade.
7. **All billing-relevant time comes from `IVirtualClock`**, never system time — this is both a testability requirement and the basis of the demo's "time travel" feature.
8. **No real payment data ever.** Card numbers are a fixed deterministic table (see `docs/research/M0-Billing-Platform-Research-Brief.md` §2.6) mapping to outcomes, not real card validation.

## Observability

Serilog (structured logs, bootstrap-logger pattern, `UseSerilogRequestLogging`) + OpenTelemetry tracing/metrics (CMMS already proved this combination isn't used together elsewhere in the portfolio — doing both here is the project's observability differentiator). Correlation ID flows: inbound request → webhook processing → subscription change → invoice → payment attempt → retry, so a single trace can be followed end-to-end per §11 of the project brief.

## Testing

- xUnit + `WebApplicationFactory` + `Testcontainers.PostgreSql` (real Postgres in integration tests — Fleet/CMMS/Order&Inventory convention; HelpDesk's EF-InMemory approach is the outlier and is *not* being followed here given the concurrency/constraint-heavy domain).
- `*.ArchitectureTests` (NetArchTest) enforcing module boundaries from day one.
- High-value scenarios (see `docs/ROADMAP.md` test strategy): duplicate webhook → one effect; two renewal workers → one invoice; duplicate payment request → one effect; cross-tenant access → denied; failed payment → retry policy fires correctly; out-of-order event → safe.
- Frontend: Vitest + Testing Library (colocated tests, portfolio convention), plus Playwright for the core paid-invoice and subscription-upgrade flows given the financial stakes (only Order & Inventory has e2e today — this project earns it).

## Git

Human-only public commit history (no AI co-author attribution), small coherent commits per milestone, per the project's global instructions.
