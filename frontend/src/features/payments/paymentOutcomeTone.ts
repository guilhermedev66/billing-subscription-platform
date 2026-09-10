import type { BillingStatusTone } from '@/components/ui/StatusBadge'
import type { PaymentOutcome } from './types'

/**
 * Maps onto the EXISTING billingStatusTokens tones — never invent new ones
 * per module. `requires_3ds` is `info` (pending), not `destructive` — it's a
 * mid-challenge state, not a decline; the invoice isn't touched by it.
 */
export const PAYMENT_OUTCOME_TONE: Record<PaymentOutcome, BillingStatusTone> = {
  succeeded: 'success',
  declined: 'destructive',
  insufficient_funds: 'destructive',
  expired: 'destructive',
  requires_3ds: 'info',
  processing_error: 'destructive',
}

export const PAYMENT_OUTCOME_LABEL: Record<PaymentOutcome, string> = {
  succeeded: 'Succeeded',
  declined: 'Declined',
  insufficient_funds: 'Insufficient funds',
  expired: 'Expired card',
  requires_3ds: 'Awaiting 3D Secure confirmation',
  processing_error: 'Processing error',
}
