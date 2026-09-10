import { api } from '@/lib/api/client'
import { idempotencyHeaders } from '@/lib/idempotency'
import type { Invoice, InvoiceLineItem, InvoiceLineType, InvoiceStatus } from './types'

/**
 * Billing module endpoints, aligned to the real BillingPlatform.Billing.Api
 * contract (BillingEndpoints.cs / BillingContracts.cs): list, get, and void
 * (POST /invoices/{id}/void — owned draft/open invoice -> 200, foreign/missing
 * -> 404, invalid/concurrent state -> 409; a real state transition, so it
 * carries an Idempotency-Key like every other mutation in this app). As with
 * Subscriptions/Catalog, every enum here is a C# enum with no
 * JsonStringEnumConverter registered server-side, so it serializes as its
 * numeric ordinal — never as a string. This file is the ONLY place those
 * numeric wire shapes exist; the rest of the app works with the friendly
 * string shapes in ./types.
 */

type WireInvoiceStatus = 0 | 1 | 2 | 3 | 4

const STATUS_FROM_WIRE: Record<WireInvoiceStatus, InvoiceStatus> = {
  0: 'draft',
  1: 'open',
  2: 'paid',
  3: 'void',
  4: 'uncollectible',
}

type WireInvoiceLineType = 0 | 1 | 2 | 3

const LINE_TYPE_FROM_WIRE: Record<WireInvoiceLineType, InvoiceLineType> = {
  0: 'base',
  1: 'proration_debit',
  2: 'proration_credit',
  3: 'credit_note',
}

interface WireInvoiceLineItem {
  id: string
  description: string
  amountCents: number
  lineType: WireInvoiceLineType
}

export interface WireInvoice {
  id: string
  organizationId: string
  customerId: string
  subscriptionId: string | null
  status: WireInvoiceStatus
  invoiceNumber: string
  currency: string
  lineItems: WireInvoiceLineItem[]
  subtotal: number
  total: number
  issueDate: string
  dueDate: string
  paidAt: string | null
  createdAt: string
  dunningAttemptCount: number
  nextRetryAt: string | null
}

function lineItemFromWire(wire: WireInvoiceLineItem): InvoiceLineItem {
  return {
    id: wire.id,
    description: wire.description,
    amountCents: wire.amountCents,
    lineType: LINE_TYPE_FROM_WIRE[wire.lineType],
  }
}

/** Exported for features/payments/api.ts, which returns an invoice alongside every payment attempt. */
export function invoiceFromWire(wire: WireInvoice): Invoice {
  return {
    id: wire.id,
    customerId: wire.customerId,
    subscriptionId: wire.subscriptionId,
    status: STATUS_FROM_WIRE[wire.status],
    invoiceNumber: wire.invoiceNumber,
    currency: wire.currency,
    lineItems: wire.lineItems.map(lineItemFromWire),
    subtotalCents: wire.subtotal,
    totalCents: wire.total,
    issueDate: wire.issueDate,
    dueDate: wire.dueDate,
    paidAt: wire.paidAt,
    createdAt: wire.createdAt,
    dunningAttemptCount: wire.dunningAttemptCount,
    nextRetryAt: wire.nextRetryAt,
  }
}

export async function listInvoices(): Promise<Invoice[]> {
  const wireInvoices = await api.get<WireInvoice[]>('/invoices/')
  return wireInvoices.map(invoiceFromWire)
}

/** Cross-tenant access returns 404, not 403 — org scoping is enforced server-side via the JWT org_id claim. */
export async function getInvoice(id: string): Promise<Invoice> {
  const wireInvoice = await api.get<WireInvoice>(`/invoices/${id}`)
  return invoiceFromWire(wireInvoice)
}

/** Only a draft or open invoice can be voided; the backend returns 409 for any other state or a concurrent change. */
export async function voidInvoice(id: string): Promise<Invoice> {
  const wireInvoice = await api.post<WireInvoice>(`/invoices/${id}/void`, undefined, { headers: idempotencyHeaders() })
  return invoiceFromWire(wireInvoice)
}
