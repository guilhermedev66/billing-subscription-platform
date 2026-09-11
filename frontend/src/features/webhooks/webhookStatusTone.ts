import type { BillingStatusTone } from '@/components/ui/StatusBadge'
import type { WebhookEventDisplayStatus } from './types'

/** Maps onto the EXISTING billingStatusTokens tones — never invent new ones per module. */
export const WEBHOOK_EVENT_STATUS_TONE: Record<WebhookEventDisplayStatus, BillingStatusTone> = {
  delivered: 'success',
  pending: 'info',
  retrying: 'warning',
}

export const WEBHOOK_EVENT_STATUS_LABEL: Record<WebhookEventDisplayStatus, string> = {
  delivered: 'Delivered',
  pending: 'Pending',
  retrying: 'Retrying',
}

/**
 * A single delivery attempt has no status field either — just statusCode/error.
 * Derived inline rather than via a lookup table since there are only two cases.
 */
export function deliveryAttemptTone(attempt: { statusCode: number | null; error: string | null }): BillingStatusTone {
  return attempt.error === null && attempt.statusCode !== null && attempt.statusCode >= 200 && attempt.statusCode < 300
    ? 'success'
    : 'destructive'
}
