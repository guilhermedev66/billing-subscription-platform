import { http, HttpResponse } from 'msw'
import { getInvoiceForMock, updateInvoiceForMock, type WireInvoice } from './invoicesHandlers'
import { setSubscriptionStatusForPayment, SUBSCRIPTION_STATUS_WIRE } from './subscriptionsHandlers'

/**
 * Stands in for the real BillingPlatform.Payments.Api (backend/src/Modules/Payments).
 * Replicates PaymentService.AttemptInternalAsync and Invoice.RecordPaymentFailure:
 * `declined`/`insufficient_funds`/`expired`/`processing_error` all go through the
 * IDENTICAL dunning-failure path — none of them throw, none of them skip dunning,
 * none of them auto-resolve to success. `succeeded` marks the invoice paid outright.
 *
 * `requires_3ds` is stateful and different (contract correction from Codex —
 * Backend mid-M4, replacing an earlier "treat it like a decline" assumption):
 * the FIRST attempt with that card returns `requires_3ds` and leaves the invoice
 * completely untouched (no dunning effect — it's a pending challenge, not a
 * failure). A SECOND attempt against the same invoice with the SAME card
 * resolves as `succeeded`, simulating the customer completing an OTP challenge.
 * Attempting with a different card cancels the pending challenge.
 *
 * GET /invoices/{id}/attempts returns the durable, per-invoice attempt ledger
 * (oldest first) — every attempt made through the attempt endpoint below is
 * recorded into it. There is still no separate retry endpoint — a retry is
 * just calling attempt again with a card.
 *
 * Dunning schedule mirrors Invoice.RecordPaymentFailure exactly: 1st failure sets
 * an anchor and retries in 3 days; 2nd retries 7 days from the anchor; 3rd retries
 * 14 days from the anchor; the 4th exhausts and marks the invoice uncollectible.
 */
const DAY_MS = 24 * 60 * 60 * 1000

type WirePaymentOutcome = 0 | 1 | 2 | 3 | 4 | 5
const SUCCEEDED: WirePaymentOutcome = 0
const DECLINED: WirePaymentOutcome = 1
const INSUFFICIENT_FUNDS: WirePaymentOutcome = 2
const EXPIRED: WirePaymentOutcome = 3
const REQUIRES_3DS: WirePaymentOutcome = 4
const PROCESSING_ERROR: WirePaymentOutcome = 5

interface WirePaymentAttempt {
  id: string
  organizationId: string
  invoiceId: string
  cardNumberLast4: string
  outcome: WirePaymentOutcome
  attemptedAt: string
  attemptNumber: number
}

const CARDS: Record<string, { outcome: WirePaymentOutcome; last4: string }> = {
  '4242424242424242': { outcome: SUCCEEDED, last4: '4242' },
  '4000000000000002': { outcome: DECLINED, last4: '0002' },
  '4000000000000004': { outcome: INSUFFICIENT_FUNDS, last4: '0004' },
  '4000000000000005': { outcome: EXPIRED, last4: '0005' },
  '4000000000003022': { outcome: REQUIRES_3DS, last4: '3022' },
  '4000000000000007': { outcome: PROCESSING_ERROR, last4: '0007' },
}

function problem(status: number, title: string, detail?: string, errors?: Record<string, string[]>) {
  return HttpResponse.json({ status, title, detail, errors }, { status })
}

let nextAttemptSeq = 1

/** Mock-only bookkeeping — the anchor date isn't part of the wire shape, only DunningAttemptCount/NextRetryAt derive from it. */
const dunningAnchors = new Map<string, string>()

/** Mock-only bookkeeping — which card (if any) is awaiting a 3D Secure challenge confirmation for a given invoice. */
const pendingThreeDsChallenges = new Map<string, string>()

/** The durable, per-invoice attempt ledger GET /invoices/{id}/attempts reads from — oldest first. */
const attemptsByInvoice = new Map<string, WirePaymentAttempt[]>()

function makeAttempt(invoice: WireInvoice, last4: string, outcome: WirePaymentOutcome, attemptedAt: string): WirePaymentAttempt {
  const attempt: WirePaymentAttempt = {
    id: `pay_${nextAttemptSeq++}`,
    organizationId: invoice.organizationId,
    invoiceId: invoice.id,
    cardNumberLast4: last4,
    outcome,
    attemptedAt,
    attemptNumber: invoice.dunningAttemptCount + 1,
  }
  const history = attemptsByInvoice.get(invoice.id) ?? []
  history.push(attempt)
  attemptsByInvoice.set(invoice.id, history)
  return attempt
}

function completeSuccess(invoice: WireInvoice, last4: string, attemptedAt: string) {
  const attempt = makeAttempt(invoice, last4, SUCCEEDED, attemptedAt)
  const updated = updateInvoiceForMock(invoice.id, { status: 2, paidAt: attemptedAt, dunningAttemptCount: 0, nextRetryAt: null })!
  if (invoice.subscriptionId) {
    setSubscriptionStatusForPayment(invoice.subscriptionId, SUBSCRIPTION_STATUS_WIRE.ACTIVE)
  }
  return { attempt, invoice: updated }
}

function recordPaymentFailure(invoice: WireInvoice, attemptedAt: string): Partial<WireInvoice> {
  if (invoice.dunningAttemptCount === 0) {
    dunningAnchors.set(invoice.id, attemptedAt)
    return { dunningAttemptCount: 1, nextRetryAt: new Date(new Date(attemptedAt).getTime() + 3 * DAY_MS).toISOString() }
  }

  const attemptCount = invoice.dunningAttemptCount + 1
  const anchor = dunningAnchors.get(invoice.id) ?? attemptedAt
  const nextRetryAt =
    attemptCount === 2
      ? new Date(new Date(anchor).getTime() + 7 * DAY_MS).toISOString()
      : attemptCount === 3
        ? new Date(new Date(anchor).getTime() + 14 * DAY_MS).toISOString()
        : null

  if (attemptCount >= 4) {
    return { dunningAttemptCount: attemptCount, nextRetryAt: null, status: 4 }
  }

  return { dunningAttemptCount: attemptCount, nextRetryAt }
}

function chargeInvoice(invoice: WireInvoice, cardNumber: string) {
  const card = CARDS[cardNumber]
  const now = new Date().toISOString()

  const isConfirmingChallenge = card.outcome === REQUIRES_3DS && pendingThreeDsChallenges.get(invoice.id) === cardNumber
  if (isConfirmingChallenge) {
    pendingThreeDsChallenges.delete(invoice.id)
    return completeSuccess(invoice, card.last4, now)
  }

  // Any attempt other than a matching challenge-confirmation supersedes a stale pending challenge.
  pendingThreeDsChallenges.delete(invoice.id)

  if (card.outcome === SUCCEEDED) {
    return completeSuccess(invoice, card.last4, now)
  }

  if (card.outcome === REQUIRES_3DS) {
    // First attempt with this card: a pending challenge, not a failure — invoice is untouched.
    pendingThreeDsChallenges.set(invoice.id, cardNumber)
    return { attempt: makeAttempt(invoice, card.last4, REQUIRES_3DS, now), invoice }
  }

  const attempt = makeAttempt(invoice, card.last4, card.outcome, now)
  const patch = recordPaymentFailure(invoice, now)
  const updated = updateInvoiceForMock(invoice.id, patch)!

  if (invoice.subscriptionId) {
    setSubscriptionStatusForPayment(
      invoice.subscriptionId,
      updated.status === 4 ? SUBSCRIPTION_STATUS_WIRE.UNPAID : SUBSCRIPTION_STATUS_WIRE.PAST_DUE,
    )
  }

  return { attempt, invoice: updated }
}

export const paymentsHandlers = [
  http.get('/api/payments/invoices/:id/attempts', ({ params }) => {
    const invoiceId = params.id as string
    if (!getInvoiceForMock(invoiceId)) return problem(404, 'Invoice not found.')
    return HttpResponse.json(attemptsByInvoice.get(invoiceId) ?? [])
  }),

  http.post('/api/payments/invoices/:id/attempt', async ({ params, request }) => {
    const invoiceId = params.id as string
    const invoice = getInvoiceForMock(invoiceId)
    if (!invoice) return problem(404, 'Invoice not found.')
    if (invoice.status === 2 || invoice.status === 3 || invoice.status === 4) {
      return problem(400, 'Only open invoices can be charged.', undefined, { invoiceId: ['Only open invoices can be charged.'] })
    }

    const body = (await request.json()) as { cardNumber?: string }
    const normalized = (body.cardNumber ?? '').replace(/\s+/g, '')
    if (!CARDS[normalized]) {
      return problem(400, 'Validation failed.', undefined, { cardNumber: ['The card number is not one of the supported simulator cards.'] })
    }

    return HttpResponse.json(chargeInvoice(invoice, normalized))
  }),
]
