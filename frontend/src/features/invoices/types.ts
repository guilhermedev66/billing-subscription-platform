/**
 * Invoice domain — field names/shape mirror BillingPlatform.Billing.Application's
 * InvoiceSummary record exactly (backend/src/Modules/Billing/.../BillingContracts.cs),
 * confirmed against the real Invoice domain model (Billing.Domain/Invoice.cs).
 *
 * There is NO tax concept anywhere in this domain — Total always equals Subtotal
 * (Invoice.cs: `public long Total => Subtotal;`). Don't reintroduce a tax field;
 * an earlier speculative version of this file guessed one before the backend
 * existed and it was wrong.
 *
 * Dunning state (dunningAttemptCount/nextRetryAt) lives directly on the invoice,
 * not as a separate object — there is no InvoiceDunning shape. nextRetryAt is
 * null once dunning is exhausted (status -> uncollectible) or the invoice is
 * recovered (status -> paid).
 */
export type InvoiceStatus = 'draft' | 'open' | 'paid' | 'void' | 'uncollectible'

export type InvoiceLineType = 'base' | 'proration_debit' | 'proration_credit' | 'credit_note'

export interface InvoiceLineItem {
  id: string
  description: string
  amountCents: number
  lineType: InvoiceLineType
}

export interface Invoice {
  id: string
  customerId: string
  subscriptionId: string | null
  status: InvoiceStatus
  /** Sequential per-org display id, e.g. "INV-2026-0001". */
  invoiceNumber: string
  currency: string
  lineItems: InvoiceLineItem[]
  /** Sum of all line items. Always equal to totalCents — no tax in this domain. */
  subtotalCents: number
  totalCents: number
  issueDate: string
  dueDate: string
  paidAt: string | null
  createdAt: string
  dunningAttemptCount: number
  nextRetryAt: string | null
}
