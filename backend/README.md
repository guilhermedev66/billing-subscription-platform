# Billing Platform backend

.NET 10 modular-monolith backend for the billing simulator: organizations,
customers, product/price catalog, subscription lifecycle with proration,
invoicing, a deterministic simulated payment gateway with dunning, webhook
delivery with HMAC signing, a virtual-clock "time travel" simulation console,
and MRR/ARR/waterfall reporting.

## Structure

One schema + one `DbContext` per module, cross-module calls only through
another module's Application-layer contracts (no cross-schema FKs). See
`../docs/ARCHITECTURE.md` for the full module map and domain invariants.

- `src/Host/BillingPlatform.Api` is the composition root.
- `src/Modules/*` — Identity, Organizations, Customers, Catalog,
  Subscriptions, Billing, Payments, Webhooks, SimulationClock, Reporting.
  Each follows `Domain` → `Application` → `Infrastructure` → `Api`.
- `tests/BillingPlatform.ArchitectureTests` enforces dependency direction and
  module isolation (NetArchTest).
- `tests/BillingPlatform.UnitTests` and `tests/BillingPlatform.IntegrationTests`
  (real PostgreSQL via Testcontainers) cover domain logic and the
  concurrency/idempotency/tenant-isolation scenarios listed in
  `../docs/ROADMAP.md`.

Build and test from this directory:

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build
```

Integration tests need a real PostgreSQL reachable via Testcontainers
(Docker). Running the API or EF Core tooling outside Compose requires
`ConnectionStrings__BillingPlatform` from an environment variable or user
secret; no database password is stored in tracked application settings.

## Run with Docker Compose

From the repo root, create a `.env` file (gitignored) with:

```bash
POSTGRES_PASSWORD=<a local dev password>
JWT_SIGNING_KEY=$(openssl rand -base64 48)
```

Then start Postgres, the API, and the web frontend together:

```bash
docker compose up --build
```

- API: `http://localhost:8080` — liveness/readiness at `/health/live` and
  `/health/ready`; OpenAPI at `/openapi/v1.json` (development only).
- Web: `http://localhost:5173`

The API deliberately refuses to start when `Jwt__SigningKey` is missing or
shorter than 32 UTF-8 bytes.

A session's earliest organization membership is used as the primary tenant.
Explicit multi-organization switching is not implemented — this is a
single-operator portfolio demo, not a multi-tenant SaaS product.

## Simulated payments and virtual time

There is no real payment processing. `Payments` uses a fixed, deterministic
test-card-number table (see `../docs/research/M0-Billing-Platform-Research-Brief.md`
§2.6) to produce outcomes (paid, declined, insufficient funds, expired,
requires-3DS, transient error). `IVirtualClock` replaces system time for all
billing-relevant logic; the Simulation Console (gated behind a
`simulation-operator` JWT claim) can fast-forward it to demo renewals,
dunning retries, and webhook delivery without waiting on real calendar time.
