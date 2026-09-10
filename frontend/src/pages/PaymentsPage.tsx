import { useQuery } from '@tanstack/react-query'
import { Clock, ShieldCheck } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { Button } from '@/components/ui/Button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { ErrorState } from '@/components/ui/ErrorState'
import { Modal } from '@/components/ui/Modal'
import { CardSkeletonGrid } from '@/components/ui/Skeleton'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { listCustomers } from '@/features/customers/api'
import type { Customer } from '@/features/customers/types'
import { listInvoices } from '@/features/invoices/api'
import { relativeTime } from '@/features/invoices/dunningCadence'
import { INVOICE_STATUS_LABEL, invoiceRiskTone } from '@/features/invoices/invoiceStatusTone'
import type { Invoice } from '@/features/invoices/types'
import { PAYMENT_OUTCOME_LABEL, PAYMENT_OUTCOME_TONE } from '@/features/payments/paymentOutcomeTone'
import { useRecentAttempts } from '@/features/payments/recentAttempts'
import { TestCardSelector } from '@/features/payments/TestCardSelector'
import { formatCents } from '@/lib/money'
import { useNow } from '@/lib/useNow'

const absoluteDateFormatter = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeStyle: 'short' })

/**
 * Invoices with an active, failing dunning cycle (open + at least one failed
 * attempt) or exhausted retries (uncollectible) — the set that actually needs
 * operator attention. A brand-new open invoice with zero attempts isn't "at
 * risk" yet, it's just a normal unpaid bill.
 */
function isAtRisk(invoice: Invoice): boolean {
  return (invoice.status === 'open' && invoice.dunningAttemptCount > 0) || invoice.status === 'uncollectible'
}

/** Soonest nextRetryAt first; uncollectible invoices have no nextRetryAt, so they sort to the end. */
function byUrgency(a: Invoice, b: Invoice): number {
  if (a.nextRetryAt && b.nextRetryAt) return new Date(a.nextRetryAt).getTime() - new Date(b.nextRetryAt).getTime()
  if (a.nextRetryAt) return -1
  if (b.nextRetryAt) return 1
  return 0
}

export function PaymentsPage() {
  const {
    data: invoices,
    isPending: invoicesPending,
    isError: invoicesError,
    refetch: refetchInvoices,
  } = useQuery({ queryKey: ['invoices'], queryFn: listInvoices })

  const {
    data: customers,
    isPending: customersPending,
    isError: customersError,
    refetch: refetchCustomers,
  } = useQuery({ queryKey: ['customers'], queryFn: listCustomers })

  const attempts = useRecentAttempts()
  const now = useNow(60_000)

  const [retryInvoiceId, setRetryInvoiceId] = useState<string | null>(null)

  const customerById = useMemo(() => {
    const map = new Map<string, Customer>()
    for (const customer of customers ?? []) map.set(customer.id, customer)
    return map
  }, [customers])

  const invoiceById = useMemo(() => {
    const map = new Map<string, Invoice>()
    for (const invoice of invoices ?? []) map.set(invoice.id, invoice)
    return map
  }, [invoices])

  const atRiskInvoices = useMemo(() => {
    return (invoices ?? []).filter(isAtRisk).sort(byUrgency)
  }, [invoices])

  const isPending = invoicesPending || customersPending
  const isError = invoicesError || customersError
  const refetch = () => {
    refetchInvoices()
    refetchCustomers()
  }

  const retryInvoice = retryInvoiceId ? invoiceById.get(retryInvoiceId) : undefined

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold tracking-tight text-foreground">Payments</h1>
      </div>

      {isPending && <CardSkeletonGrid count={4} label="Loading payments" />}

      {isError && <ErrorState message="Couldn't load payments." onRetry={refetch} />}

      {!isPending && !isError && invoices && customers && (
        <>
          <Card>
            <CardHeader>
              <CardTitle>At-risk invoices</CardTitle>
            </CardHeader>
            <CardContent>
              {atRiskInvoices.length === 0 ? (
                <EmptyState
                  icon={ShieldCheck}
                  title="Nothing needs attention"
                  description="No invoices are currently in a dunning cycle."
                />
              ) : (
                <div className="overflow-x-auto rounded-lg border border-border">
                  <table className="w-full text-left text-sm">
                    <thead className="border-b border-border bg-surface-muted">
                      <tr>
                        <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                          Customer
                        </th>
                        <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                          Invoice
                        </th>
                        <th scope="col" className="px-4 py-2 text-right font-medium text-muted-foreground">
                          Total
                        </th>
                        <th scope="col" className="px-4 py-2 text-right font-medium text-muted-foreground">
                          Attempts
                        </th>
                        <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                          Next retry
                        </th>
                        <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                          Status
                        </th>
                        <th scope="col" className="px-4 py-2 text-right font-medium text-muted-foreground">
                          <span className="sr-only">Actions</span>
                        </th>
                      </tr>
                    </thead>
                    <tbody>
                      {atRiskInvoices.map((invoice) => {
                        const customer = customerById.get(invoice.customerId)

                        return (
                          <tr
                            key={invoice.id}
                            className="relative border-b border-border last:border-0 hover:bg-surface-muted focus-within:bg-surface-muted"
                          >
                            <td className="px-4 py-3 font-medium text-foreground">
                              <Link
                                to={`/invoices/${invoice.id}`}
                                aria-label={`View invoice ${invoice.invoiceNumber} for ${customer?.name ?? invoice.customerId}`}
                              >
                                <span className="absolute inset-0" aria-hidden="true" />
                                {customer?.name ?? invoice.customerId}
                              </Link>
                            </td>
                            <td className="px-4 py-3 text-muted-foreground">{invoice.invoiceNumber}</td>
                            <td className="px-4 py-3 text-right font-mono tabular-nums text-foreground">
                              {formatCents(invoice.totalCents, invoice.currency)}
                            </td>
                            <td className="px-4 py-3 text-right font-mono tabular-nums text-foreground">
                              {invoice.dunningAttemptCount}
                            </td>
                            <td className="px-4 py-3 text-muted-foreground">
                              {invoice.nextRetryAt ? (
                                <div className="flex flex-col">
                                  <span>{absoluteDateFormatter.format(new Date(invoice.nextRetryAt))}</span>
                                  <span className="text-xs">{relativeTime(invoice.nextRetryAt, now)}</span>
                                </div>
                              ) : (
                                <span>Exhausted</span>
                              )}
                            </td>
                            <td className="px-4 py-3">
                              <StatusBadge tone={invoiceRiskTone(invoice)}>{INVOICE_STATUS_LABEL[invoice.status]}</StatusBadge>
                            </td>
                            <td className="relative z-10 px-4 py-3 text-right">
                              <Button
                                variant="secondary"
                                size="sm"
                                aria-label={`Retry invoice ${invoice.invoiceNumber} for ${customer?.name ?? invoice.customerId}`}
                                onClick={() => setRetryInvoiceId(invoice.id)}
                              >
                                Retry now
                              </Button>
                            </td>
                          </tr>
                        )
                      })}
                    </tbody>
                  </table>
                </div>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Recent activity</CardTitle>
            </CardHeader>
            <CardContent>
              {attempts.length === 0 ? (
                <EmptyState
                  icon={Clock}
                  title="No payment attempts yet this session"
                  description="Retries you make here or on an invoice's detail page will show up here."
                />
              ) : (
                <div className="overflow-x-auto rounded-lg border border-border">
                  <table className="w-full text-left text-sm">
                    <thead className="border-b border-border bg-surface-muted">
                      <tr>
                        <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                          Outcome
                        </th>
                        <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                          Invoice
                        </th>
                        <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                          Card
                        </th>
                        <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                          When
                        </th>
                      </tr>
                    </thead>
                    <tbody>
                      {attempts.map((attempt) => {
                        const invoice = invoiceById.get(attempt.invoiceId)

                        return (
                          <tr key={attempt.id} className="border-b border-border last:border-0">
                            <td className="px-4 py-3">
                              <StatusBadge tone={PAYMENT_OUTCOME_TONE[attempt.outcome]}>
                                {PAYMENT_OUTCOME_LABEL[attempt.outcome]}
                              </StatusBadge>
                            </td>
                            <td className="px-4 py-3 text-muted-foreground">
                              <Link to={`/invoices/${attempt.invoiceId}`} className="font-medium text-foreground hover:underline">
                                {invoice?.invoiceNumber ?? attempt.invoiceId}
                              </Link>
                            </td>
                            <td className="px-4 py-3 text-muted-foreground">•••• {attempt.cardNumberLast4}</td>
                            <td className="px-4 py-3 text-muted-foreground">
                              {absoluteDateFormatter.format(new Date(attempt.attemptedAt))}
                            </td>
                          </tr>
                        )
                      })}
                    </tbody>
                  </table>
                </div>
              )}
            </CardContent>
          </Card>
        </>
      )}

      {/*
        A future Developers/Settings page could expose POST /api/payments/dunning-sweep
        (org-wide time-travel-console ops action) — it's not a per-invoice retry and
        intentionally has no UI here.
      */}

      <Modal
        open={retryInvoiceId !== null}
        onClose={() => setRetryInvoiceId(null)}
        title={retryInvoice ? `Retry ${retryInvoice.invoiceNumber}` : 'Retry invoice'}
        description={
          retryInvoice ? `${customerById.get(retryInvoice.customerId)?.name ?? retryInvoice.customerId} — ${formatCents(retryInvoice.totalCents, retryInvoice.currency)}` : undefined
        }
        footer={
          <Button type="button" variant="secondary" onClick={() => setRetryInvoiceId(null)}>
            Close
          </Button>
        }
      >
        {retryInvoice && (
          <TestCardSelector
            invoiceId={retryInvoice.id}
            chargeable={retryInvoice.status === 'open'}
            disabledReason={retryInvoice.status === 'uncollectible' ? 'Retries are exhausted for this invoice.' : undefined}
          />
        )}
      </Modal>
    </div>
  )
}
