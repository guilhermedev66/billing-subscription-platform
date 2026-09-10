/**
 * Every payment- and subscription-mutating request needs an Idempotency-Key
 * header (docs/ARCHITECTURE.md invariant #2) — the backend persists the key
 * with a DB-unique constraint so a duplicate request (double-click, network
 * retry) replays the stored response instead of duplicating the billing
 * effect. One key per logical user action — generate it fresh at the call
 * site, not reused across separate retries the user explicitly triggers.
 */
export function idempotencyHeaders(): { 'Idempotency-Key': string } {
  return { 'Idempotency-Key': crypto.randomUUID() }
}
