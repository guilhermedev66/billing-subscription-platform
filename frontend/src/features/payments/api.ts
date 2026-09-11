import type { Invoice } from '@/features/invoices/types'
import { invoiceFromWire, type WireInvoice } from '@/features/invoices/api'
import { api } from '@/lib/api/client'
import { idempotencyHeaders } from '@/lib/idempotency'
import type { PaymentAttempt, PaymentOutcome } from './types'

/**
 * Payments module endpoints, aligned to the real BillingPlatform.Payments.Api
 * contract (PaymentsEndpoints.cs / PaymentContracts.cs). GET
 * /invoices/{invoiceId}/attempts is the durable, tenant-scoped attempt
 * ledger (404 only cross-tenant/missing) — this is the source of truth for
 * "what happened to this invoice," and survives a reload, unlike
 * features/payments/recentAttempts.ts's session-local store (which stays
 * useful only as a transient "just happened in this browser tab" supplement
 * for cross-invoice views, since there's no org-wide history endpoint).
 * There is still no separate "retry" endpoint — a retry is just calling
 * attempt again with a card (same endpoint). sweepDunning wraps the real
 * POST /api/payments/dunning-sweep (org-wide, Idempotency-Key required) —
 * it's an M5 time-travel-console ops action the Simulation Bar's "Process
 * Dunning Sweep" batch operation calls, not something a user triggers per
 * invoice, which is why it lives here rather than a per-invoice UI.
 */

type WirePaymentOutcome = 0 | 1 | 2 | 3 | 4 | 5

const OUTCOME_FROM_WIRE: Record<WirePaymentOutcome, PaymentOutcome> = {
  0: 'succeeded',
  1: 'declined',
  2: 'insufficient_funds',
  3: 'expired',
  4: 'requires_3ds',
  5: 'processing_error',
}

interface WirePaymentAttempt {
  id: string
  organizationId: string
  invoiceId: string
  cardNumberLast4: string
  outcome: WirePaymentOutcome
  attemptedAt: string
  attemptNumber: number
}

interface WirePaymentResult {
  attempt: WirePaymentAttempt
  invoice: WireInvoice
}

function attemptFromWire(wire: WirePaymentAttempt): PaymentAttempt {
  return {
    id: wire.id,
    invoiceId: wire.invoiceId,
    cardNumberLast4: wire.cardNumberLast4,
    outcome: OUTCOME_FROM_WIRE[wire.outcome],
    attemptedAt: wire.attemptedAt,
    attemptNumber: wire.attemptNumber,
  }
}

export interface PaymentAttemptResult {
  attempt: PaymentAttempt
  invoice: Invoice
}

/**
 * Charges an invoice with one of the deterministic test cards (src/features/payments/testCards.ts).
 * Also how a dunning retry works — there is no separate retry call, just attempt again with a card.
 */
export async function attemptPayment(invoiceId: string, cardNumber: string): Promise<PaymentAttemptResult> {
  const wireResult = await api.post<WirePaymentResult>(
    `/payments/invoices/${invoiceId}/attempt`,
    { cardNumber },
    { headers: idempotencyHeaders() },
  )
  return { attempt: attemptFromWire(wireResult.attempt), invoice: invoiceFromWire(wireResult.invoice) }
}

/** The durable per-invoice attempt ledger — ordered oldest-first, same as the backend returns it. */
export async function listPaymentAttempts(invoiceId: string): Promise<PaymentAttempt[]> {
  const wireAttempts = await api.get<WirePaymentAttempt[]>(`/payments/invoices/${invoiceId}/attempts`)
  return wireAttempts.map(attemptFromWire)
}

interface WireDunningSweepAttempt {
  attempt: WirePaymentAttempt
  invoice: WireInvoice
}

interface WireDunningSweepResult {
  considered: number
  processed: number
  succeeded: number
  failed: number
  becameUncollectible: number
  attempts: WireDunningSweepAttempt[]
}

export interface DunningSweepResult {
  considered: number
  processed: number
  succeeded: number
  failed: number
  becameUncollectible: number
  attempts: PaymentAttemptResult[]
}

/** Real endpoint (BillingPlatform.Payments.Api's PaymentsEndpoints.SweepAsync) — org-wide, org-scoped via JWT. Charges every open invoice whose nextRetryAt is due, using the card from its most recent attempt. */
export async function sweepDunning(): Promise<DunningSweepResult> {
  const wireResult = await api.post<WireDunningSweepResult>('/payments/dunning-sweep', undefined, { headers: idempotencyHeaders() })
  return {
    considered: wireResult.considered,
    processed: wireResult.processed,
    succeeded: wireResult.succeeded,
    failed: wireResult.failed,
    becameUncollectible: wireResult.becameUncollectible,
    attempts: wireResult.attempts.map((entry) => ({ attempt: attemptFromWire(entry.attempt), invoice: invoiceFromWire(entry.invoice) })),
  }
}
