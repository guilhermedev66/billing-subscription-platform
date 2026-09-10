import { http, HttpResponse } from 'msw'

/**
 * Stands in for the real BillingPlatform.Billing.Api (backend/src/Modules/Billing).
 * Matches the real wire contract exactly: numeric InvoiceStatus/InvoiceLineType (no
 * JsonStringEnumConverter registered server-side), `invoiceNumber`/`subtotal`/`total`
 * naming, NO tax field anywhere, and dunningAttemptCount/nextRetryAt living directly
 * on the invoice. GET / (list), GET /{id}, and POST /{id}/void all exist
 * server-side — void succeeds only from draft/open, 409 otherwise.
 *
 * Seed invoices are linked to REAL customer/subscription ids from
 * customersHandlers.ts/subscriptionsHandlers.ts, covering all five InvoiceStatus
 * values, a mid-dunning open invoice, an exhausted-dunning uncollectible invoice,
 * and one worked example of proration debit/credit line items (the M3
 * upgrade/downgrade flow's financial trail). mocks/paymentsHandlers.ts mutates this
 * same store via the exported helpers below rather than duplicating it.
 */
const DAY_MS = 24 * 60 * 60 * 1000

type WireInvoiceStatus = 0 | 1 | 2 | 3 | 4
const DRAFT: WireInvoiceStatus = 0
const OPEN: WireInvoiceStatus = 1
const PAID: WireInvoiceStatus = 2
const VOID: WireInvoiceStatus = 3
const UNCOLLECTIBLE: WireInvoiceStatus = 4

type WireInvoiceLineType = 0 | 1 | 2 | 3
const BASE: WireInvoiceLineType = 0
const PRORATION_DEBIT: WireInvoiceLineType = 1
const PRORATION_CREDIT: WireInvoiceLineType = 2

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

function isoAtOffset(days: number): string {
  return new Date(Date.now() + days * DAY_MS).toISOString()
}

function problem(status: number, title: string, detail?: string, errors?: Record<string, string[]>) {
  return HttpResponse.json({ status, title, detail, errors }, { status })
}

function sumLineItems(lineItems: WireInvoiceLineItem[]): number {
  return lineItems.reduce((sum, item) => sum + item.amountCents, 0)
}

function withTotals(
  invoice: Omit<WireInvoice, 'subtotal' | 'total'>,
): WireInvoice {
  const subtotal = sumLineItems(invoice.lineItems)
  return { ...invoice, subtotal, total: subtotal }
}

const seedInvoices: WireInvoice[] = [
  withTotals({
    id: 'inv_1',
    organizationId: 'org_demo',
    customerId: 'cus_wayne',
    subscriptionId: 'sub_1',
    status: PAID,
    invoiceNumber: 'INV-2026-0001',
    currency: 'USD',
    lineItems: [{ id: 'li_1', description: 'Cloud Analytics Platform — monthly', amountCents: 2900, lineType: BASE }],
    issueDate: isoAtOffset(-10),
    dueDate: isoAtOffset(-3),
    paidAt: isoAtOffset(-9),
    createdAt: isoAtOffset(-10),
    dunningAttemptCount: 0,
    nextRetryAt: null,
  }),
  withTotals({
    id: 'inv_2',
    organizationId: 'org_demo',
    customerId: 'cus_stark',
    subscriptionId: 'sub_2',
    status: PAID,
    invoiceNumber: 'INV-2026-0002',
    currency: 'USD',
    lineItems: [{ id: 'li_2', description: 'Team Workspace — 5 seats', amountCents: 6000, lineType: BASE }],
    issueDate: isoAtOffset(-15),
    dueDate: isoAtOffset(-8),
    paidAt: isoAtOffset(-14),
    createdAt: isoAtOffset(-15),
    dunningAttemptCount: 0,
    nextRetryAt: null,
  }),
  withTotals({
    id: 'inv_3',
    organizationId: 'org_demo',
    customerId: 'cus_initech',
    subscriptionId: 'sub_4',
    status: OPEN,
    invoiceNumber: 'INV-2026-0003',
    currency: 'USD',
    lineItems: [{ id: 'li_4', description: 'Cloud Analytics Platform — monthly', amountCents: 2900, lineType: BASE }],
    issueDate: isoAtOffset(-25),
    dueDate: isoAtOffset(-18),
    paidAt: null,
    createdAt: isoAtOffset(-25),
    dunningAttemptCount: 2,
    nextRetryAt: isoAtOffset(4),
  }),
  withTotals({
    id: 'inv_4',
    organizationId: 'org_demo',
    customerId: 'cus_umbrella',
    subscriptionId: 'sub_5',
    status: UNCOLLECTIBLE,
    invoiceNumber: 'INV-2026-0004',
    currency: 'USD',
    lineItems: [{ id: 'li_5', description: 'Team Workspace — 3 seats', amountCents: 3600, lineType: BASE }],
    issueDate: isoAtOffset(-40),
    dueDate: isoAtOffset(-33),
    paidAt: null,
    createdAt: isoAtOffset(-40),
    dunningAttemptCount: 4,
    nextRetryAt: null,
  }),
  withTotals({
    id: 'inv_5',
    organizationId: 'org_demo',
    customerId: 'cus_aperture',
    subscriptionId: 'sub_6',
    status: VOID,
    invoiceNumber: 'INV-2026-0005',
    currency: 'USD',
    lineItems: [{ id: 'li_6', description: 'Cloud Analytics Platform — monthly', amountCents: 2900, lineType: BASE }],
    issueDate: isoAtOffset(-55),
    dueDate: isoAtOffset(-48),
    paidAt: null,
    createdAt: isoAtOffset(-55),
    dunningAttemptCount: 0,
    nextRetryAt: null,
  }),
  withTotals({
    id: 'inv_6',
    organizationId: 'org_demo',
    customerId: 'cus_soylent',
    subscriptionId: 'sub_7',
    status: DRAFT,
    invoiceNumber: 'INV-2026-0006',
    currency: 'USD',
    lineItems: [{ id: 'li_7', description: 'Team Workspace — 8 seats', amountCents: 9600, lineType: BASE }],
    issueDate: isoAtOffset(-1),
    dueDate: isoAtOffset(6),
    paidAt: null,
    createdAt: isoAtOffset(-1),
    dunningAttemptCount: 0,
    nextRetryAt: null,
  }),
  withTotals({
    id: 'inv_7',
    organizationId: 'org_demo',
    customerId: 'cus_hooli',
    subscriptionId: 'sub_8',
    status: PAID,
    invoiceNumber: 'INV-2026-0007',
    currency: 'USD',
    lineItems: [
      { id: 'li_8', description: 'Cloud Analytics Platform — monthly', amountCents: 2900, lineType: BASE },
      {
        id: 'li_9',
        description: 'Prorated upgrade to Cloud Analytics Platform (Sep 15–30)',
        amountCents: 1500,
        lineType: PRORATION_DEBIT,
      },
      {
        id: 'li_10',
        description: 'Unused time on previous plan (Sep 15–30)',
        amountCents: -800,
        lineType: PRORATION_CREDIT,
      },
    ],
    issueDate: isoAtOffset(-21),
    dueDate: isoAtOffset(-14),
    paidAt: isoAtOffset(-20),
    createdAt: isoAtOffset(-21),
    dunningAttemptCount: 0,
    nextRetryAt: null,
  }),
  withTotals({
    id: 'inv_8',
    organizationId: 'org_demo',
    customerId: 'cus_globex',
    subscriptionId: 'sub_3',
    status: OPEN,
    invoiceNumber: 'INV-2026-0008',
    currency: 'USD',
    lineItems: [{ id: 'li_11', description: 'Cloud Analytics Platform — annual', amountCents: 29000, lineType: BASE }],
    issueDate: isoAtOffset(-2),
    dueDate: isoAtOffset(5),
    paidAt: null,
    createdAt: isoAtOffset(-2),
    dunningAttemptCount: 0,
    nextRetryAt: null,
  }),
  withTotals({
    id: 'inv_9',
    organizationId: 'org_demo',
    customerId: 'cus_hooli',
    subscriptionId: 'sub_8',
    status: OPEN,
    invoiceNumber: 'INV-2026-0009',
    currency: 'USD',
    lineItems: [{ id: 'li_12', description: 'Cloud Analytics Platform — monthly', amountCents: 2900, lineType: BASE }],
    issueDate: isoAtOffset(-3),
    dueDate: isoAtOffset(4),
    paidAt: null,
    createdAt: isoAtOffset(-3),
    dunningAttemptCount: 0,
    nextRetryAt: null,
  }),
]

const invoices = new Map<string, WireInvoice>(seedInvoices.map((invoice) => [invoice.id, invoice]))

/** Used by mocks/paymentsHandlers.ts — reads/mutates the same store rather than keeping a second copy. */
export function getInvoiceForMock(id: string): WireInvoice | undefined {
  return invoices.get(id)
}

export function updateInvoiceForMock(id: string, patch: Partial<WireInvoice>): WireInvoice | undefined {
  const existing = invoices.get(id)
  if (!existing) return undefined

  const merged = { ...existing, ...patch }
  const updated = patch.lineItems ? withTotals(merged) : merged
  invoices.set(id, updated)
  return updated
}

const VOIDABLE_STATUSES: WireInvoiceStatus[] = [DRAFT, OPEN]

export const invoicesHandlers = [
  http.get('/api/invoices/', () => HttpResponse.json(Array.from(invoices.values()))),

  http.get('/api/invoices/:id', ({ params }) => {
    const invoice = invoices.get(params.id as string)
    if (!invoice) return problem(404, 'Invoice not found.')
    return HttpResponse.json(invoice)
  }),

  http.post('/api/invoices/:id/void', ({ params }) => {
    const invoice = invoices.get(params.id as string)
    if (!invoice) return problem(404, 'Invoice not found.')
    if (!VOIDABLE_STATUSES.includes(invoice.status)) {
      return problem(409, 'This invoice can no longer be voided.', `Invoices in their current status can't be voided.`)
    }

    const updated = updateInvoiceForMock(invoice.id, { status: VOID, nextRetryAt: null })!
    return HttpResponse.json(updated)
  }),
]
