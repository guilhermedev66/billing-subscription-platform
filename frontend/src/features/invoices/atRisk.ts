import type { Invoice } from './types'

/**
 * Invoices with an active, failing dunning cycle (open + at least one failed
 * attempt) or exhausted retries (uncollectible) — the set that actually needs
 * operator attention. A brand-new open invoice with zero attempts isn't "at
 * risk" yet, it's just a normal unpaid bill. Shared by PaymentsPage (the
 * dunning cockpit table) and DashboardPage (the At-Risk Revenue Alert banner).
 */
export function isAtRisk(invoice: Invoice): boolean {
  return (invoice.status === 'open' && invoice.dunningAttemptCount > 0) || invoice.status === 'uncollectible'
}

/** Soonest nextRetryAt first; uncollectible invoices have no nextRetryAt, so they sort to the end. */
export function byUrgency(a: Invoice, b: Invoice): number {
  if (a.nextRetryAt && b.nextRetryAt) return new Date(a.nextRetryAt).getTime() - new Date(b.nextRetryAt).getTime()
  if (a.nextRetryAt) return -1
  if (b.nextRetryAt) return 1
  return 0
}
