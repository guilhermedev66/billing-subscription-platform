/**
 * Payments domain — field names/shape mirror BillingPlatform.Payments.Application's
 * PaymentAttemptSummary record exactly (backend/src/Modules/Payments/.../PaymentContracts.cs).
 *
 * Only `succeeded` results in the invoice being marked paid. `declined`,
 * `insufficient_funds`, `expired`, and `processing_error` are recorded via
 * the SAME dunning-failure path: dunningAttemptCount increments and
 * nextRetryAt is scheduled (or, on the 4th failure, the invoice becomes
 * uncollectible). `requires_3ds` is DIFFERENT and stateful: the first attempt
 * with that card returns `requires_3ds` and leaves the invoice completely
 * untouched (status stays `open`, no dunning effect at all — it's neither a
 * success nor a decline, it's a pending challenge). A SECOND attempt call
 * against the same invoice with the SAME card (a fresh Idempotency-Key, since
 * it's a distinct user action — simulating the customer completing the
 * challenge) then resolves as `succeeded`. See
 * `features/payments/TestCardSelector.tsx` for the "confirm challenge" step
 * that drives this. An earlier version of this comment (and this file, before
 * this contract correction) assumed `requires_3ds` behaved like a plain
 * decline — it does not.
 */
export type PaymentOutcome =
  | 'succeeded'
  | 'declined'
  | 'insufficient_funds'
  | 'expired'
  | 'requires_3ds'
  | 'processing_error'

export interface PaymentAttempt {
  id: string
  invoiceId: string
  /** Last four digits only — the backend never returns a full card number, even in this simulator. */
  cardNumberLast4: string
  outcome: PaymentOutcome
  attemptedAt: string
  /** 1-based; matches the invoice's dunningAttemptCount at the time of this attempt. */
  attemptNumber: number
}
