import type { PaymentOutcome } from './types'

export interface TestCard {
  number: string
  last4: string
  label: string
  outcome: PaymentOutcome
  outcomeLabel: string
  description: string
}

/**
 * The exact deterministic test-card table from PaymentService.Cards
 * (backend/src/Modules/Payments/.../PaymentService.cs) — do not invent card
 * numbers or outcomes beyond this table.
 *
 * `declined`, `insufficient_funds`, `expired`, and `processing_error` are all
 * recorded through the identical dunning-failure path: dunningAttemptCount
 * increments and nextRetryAt is scheduled (or, on the 4th exhausted attempt,
 * the invoice flips to uncollectible) — there is no backend special-casing
 * for "expired card that isn't retried" or "processing error that fails the
 * request with a 500." `requires_3ds` is the one genuinely stateful outcome:
 * charging with that card leaves the invoice untouched and requires a second
 * attempt with the same card (a simulated challenge confirmation — see
 * TestCardSelector.tsx) to actually succeed.
 */
export const TEST_CARDS: TestCard[] = [
  {
    number: '4242424242424242',
    last4: '4242',
    label: 'Visa •••• 4242',
    outcome: 'succeeded',
    outcomeLabel: 'Succeeded',
    description: 'Payment succeeds immediately. Invoice moves to paid.',
  },
  {
    number: '4000000000000002',
    last4: '0002',
    label: 'Visa •••• 0002',
    outcome: 'declined',
    outcomeLabel: 'Card declined',
    description: 'Generic decline. Invoice stays open and a dunning retry is scheduled.',
  },
  {
    number: '4000000000000004',
    last4: '0004',
    label: 'Visa •••• 0004',
    outcome: 'insufficient_funds',
    outcomeLabel: 'Insufficient funds',
    description: 'Decline for insufficient funds. Invoice stays open and a dunning retry is scheduled.',
  },
  {
    number: '4000000000000005',
    last4: '0005',
    label: 'Visa •••• 0005',
    outcome: 'expired',
    outcomeLabel: 'Expired card',
    description: 'Card has expired. Invoice stays open and a dunning retry is scheduled, same as any other decline.',
  },
  {
    number: '4000000000003022',
    last4: '3022',
    label: 'Visa •••• 3022',
    outcome: 'requires_3ds',
    outcomeLabel: '3D Secure required',
    description:
      'Simulates an OTP challenge step. The invoice stays open and untouched (no dunning effect) until a second charge with this same card confirms the simulated challenge — then it succeeds.',
  },
  {
    number: '4000000000000007',
    last4: '0007',
    label: 'Visa •••• 0007',
    outcome: 'processing_error',
    outcomeLabel: 'Processing error',
    description:
      'A gateway-side processing failure, recorded the same way as a decline — the request itself still succeeds (HTTP 200). Invoice stays open and a dunning retry is scheduled.',
  },
]
