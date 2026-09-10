# MEMORY.md — Billing & Subscription Platform

Read this first. Full detail lives in `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, and `docs/research/M0-Billing-Platform-Research-Brief.md`.

## Architecture decisions & why

- **Modular monolith, not microservices** — matches the author's other mature .NET portfolio projects (Fleet & Delivery, CMMS), avoids network overhead for no payoff at this scale.
- **One schema + one DbContext per module**, no cross-schema FKs — cross-module calls only through the other module's Application-layer contracts. Reused from Fleet/CMMS.
- **Outbox/Inbox pattern for webhooks and idempotent event ingestion** — reused from Fleet's `BuildingBlocks.Messaging` design (not the code itself, a fresh repo — just the pattern: write domain change + Event row in one transaction, background worker polls and delivers; inbound events deduped via a DB unique constraint, not an in-memory cache).
- **No Redis at MVP** — no prior project in this portfolio has an established Redis use case, and Postgres unique constraints/row locks cover idempotency and dunning state fine. Don't add it just for the resume keyword; revisit only if a real need shows up.
- **`IVirtualClock` everywhere instead of `DateTime.UtcNow`** — this is both the testability strategy and the product's "time travel" demo feature. Any billing logic that reads system time directly is a bug.
- **Money is integer cents, never float/double.** Finalized invoices are immutable — corrections are new line items/credit notes, never row edits.
- **Serilog + OpenTelemetry together** — no prior project combines both; this project's observability differentiator.

## Non-obvious rules

- Public git history is human-only — no AI co-author attribution on commits (project-specific override of the default Claude Code attribution).
- Previous portfolio projects (Fleet, CMMS, Fluxora ERP, Barber Booking, Order & Inventory, HelpDesk, TaskManagerAPI) are frozen — never modify them, only read them for pattern reference.
- Payment data is 100% synthetic — a fixed deterministic test-card table maps card numbers to outcomes (see research brief §2.6). Never attempt real card validation logic.
- **Customer `BalanceCents`/`DelinquentFlag` are immutable via the public Customers API** (both create and update always start/leave them at 0/false) — established in M2 after a QA finding that any org member could set/wipe a customer's balance through ordinary CRUD. Later milestones (Payments/Billing) must mutate these only through a dedicated ledger operation, never a generic PUT.
- **Cross-tenant lookups always return 404, never a 403/404 split** — a 403-when-exists-elsewhere pattern is a tenant-existence oracle (an M2 QA finding on the Customers module). Any new module's `GetById`/`Update` must return `NotFound()` uniformly when the resource isn't in the caller's org, regardless of whether it exists in another org.
- **This WSL shell's `dotnet`/`docker` resolve to Windows-side binaries** (build/run output paths show `C:\dev\...`). WSL-exported env vars (`FOO=bar dotnet run`, `docker compose` env) do **not** propagate across that boundary — use `launchSettings.json` profiles (`--launch-profile http`) or `appsettings.*.json` instead of env vars for the API, and a project-local gitignored `.env` file (not shell export) for `docker compose`. To reach a Windows-hosted `dotnet run`/`docker compose` service from WSL bash, use `curl.exe`, not WSL's own `curl` — plain `curl` from WSL gets connection-refused even though the service is genuinely listening.

## Worker model (Maestri canvas)

Six standing workers, redirected here from the previous project via `maestri recruit --replace` (same names reused across projects): Claude Orchestrator (this session), Codex — Backend (primary backend), Codex QA (independent reviewer), Claude — Frontend / UI (primary frontend), Antigravity (research/UX/visual/security review), Claude — Backend Fallback (takes over backend on Codex usage-limit/stall, hands back at next safe checkpoint).

**Known environment issue:** `maestri recruit --role` fails with EPERM on this machine (Windows-side `\\wsl$` UNC path bug for `/mnt/c` project dirs). Brief workers directly via `maestri ask` instead of persistent role files.
