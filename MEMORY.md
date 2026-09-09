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

## Worker model (Maestri canvas)

Six standing workers, redirected here from the previous project via `maestri recruit --replace` (same names reused across projects): Claude Orchestrator (this session), Codex — Backend (primary backend), Codex QA (independent reviewer), Claude — Frontend / UI (primary frontend), Antigravity (research/UX/visual/security review), Claude — Backend Fallback (takes over backend on Codex usage-limit/stall, hands back at next safe checkpoint).

**Known environment issue:** `maestri recruit --role` fails with EPERM on this machine (Windows-side `\\wsl$` UNC path bug for `/mnt/c` project dirs). Brief workers directly via `maestri ask` instead of persistent role files.
