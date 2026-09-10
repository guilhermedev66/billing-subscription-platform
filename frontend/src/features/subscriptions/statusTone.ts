import type { BillingStatusTone } from '@/components/ui/StatusBadge'
import type { SubscriptionStatus } from './types'

/** Maps onto the EXISTING billingStatusTokens tones — never invent new ones per module. */
export const SUBSCRIPTION_STATUS_TONE: Record<SubscriptionStatus, BillingStatusTone> = {
  trialing: 'info',
  active: 'success',
  past_due: 'warning',
  unpaid: 'destructive',
  canceled: 'neutral',
  paused: 'neutral',
}

export const SUBSCRIPTION_STATUS_LABEL: Record<SubscriptionStatus, string> = {
  trialing: 'Trialing',
  active: 'Active',
  past_due: 'Past due',
  unpaid: 'Unpaid',
  canceled: 'Canceled',
  paused: 'Paused',
}
