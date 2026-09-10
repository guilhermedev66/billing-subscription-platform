import type { BillingStatusTone } from '@/components/ui/StatusBadge'
import type { Invoice, InvoiceStatus } from './types'

/** Maps onto the EXISTING billingStatusTokens tones — never invent new ones per module. */
export const INVOICE_STATUS_TONE: Record<InvoiceStatus, BillingStatusTone> = {
  draft: 'neutral',
  open: 'info',
  paid: 'success',
  void: 'neutral',
  uncollectible: 'destructive',
}

export const INVOICE_STATUS_LABEL: Record<InvoiceStatus, string> = {
  draft: 'Draft',
  open: 'Open',
  paid: 'Paid',
  void: 'Void',
  uncollectible: 'Uncollectible',
}

/**
 * An open invoice already mid-dunning reads as "action required," not a
 * routine open bill — bumps it from info to warning without inventing a
 * status the backend doesn't have.
 */
export function invoiceRiskTone(invoice: Invoice): BillingStatusTone {
  if (invoice.status === 'open' && invoice.dunningAttemptCount > 0) return 'warning'
  return INVOICE_STATUS_TONE[invoice.status]
}
