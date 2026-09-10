import type { Invoice } from './types'

/**
 * Mirrors Invoice.RecordPaymentFailure's real schedule exactly
 * (backend/src/Modules/Billing/.../Invoice.cs): 1st failure sets an anchor
 * and retries in 3 days, 2nd retries 7 days from the anchor, 3rd retries 14
 * days from the anchor, the 4th exhausts and marks the invoice uncollectible.
 * "Day N" below is relative to the first failure, not a calendar day.
 */
export const MAX_DUNNING_ATTEMPTS = 4

export interface DunningStep {
  label: string
}

export const DUNNING_STEPS: DunningStep[] = [
  { label: 'Day 0 — Failed' },
  { label: 'Day 3 — Retry 1' },
  { label: 'Day 7 — Retry 2' },
  { label: 'Day 14 — Final' },
]

const MINUTE_MS = 60_000
const HOUR_MS = 60 * MINUTE_MS
const DAY_MS = 24 * HOUR_MS

const relativeTimeFormatter = new Intl.RelativeTimeFormat('en-US', { numeric: 'auto' })

/** "in 3 days" / "2 hours ago" style phrasing against `now` (pass Date.now(), ticked by a caller-owned interval for a live countdown). */
export function relativeTime(iso: string, now: number): string {
  const diffMs = new Date(iso).getTime() - now
  const absMs = Math.abs(diffMs)

  if (absMs < HOUR_MS) return relativeTimeFormatter.format(Math.round(diffMs / MINUTE_MS), 'minute')
  if (absMs < DAY_MS) return relativeTimeFormatter.format(Math.round(diffMs / HOUR_MS), 'hour')
  return relativeTimeFormatter.format(Math.round(diffMs / DAY_MS), 'day')
}

/**
 * An operator-facing summary distinct from the invoice's document status pill
 * (Draft/Open/Paid/Void/Uncollectible) — e.g. "Payment failing — attempt 2 of
 * 4 — next retry in 3 days". Null when there's nothing dunning-specific to
 * say (no failed attempts yet, or the invoice isn't in a dunning-relevant state).
 */
export function dunningCallout(invoice: Invoice, now: number): string | null {
  if (invoice.status === 'uncollectible') {
    return `Payment failing — retries exhausted (${MAX_DUNNING_ATTEMPTS} of ${MAX_DUNNING_ATTEMPTS}) — uncollectible`
  }
  if (invoice.status === 'open' && invoice.dunningAttemptCount > 0 && invoice.nextRetryAt) {
    return `Payment failing — attempt ${invoice.dunningAttemptCount} of ${MAX_DUNNING_ATTEMPTS} — next retry ${relativeTime(invoice.nextRetryAt, now)}`
  }
  return null
}
