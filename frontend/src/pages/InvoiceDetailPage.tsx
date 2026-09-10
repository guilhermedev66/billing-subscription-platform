import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Ban, FileText } from 'lucide-react'
import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { Button } from '@/components/ui/Button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/Card'
import { ErrorState } from '@/components/ui/ErrorState'
import { Modal } from '@/components/ui/Modal'
import { CardSkeleton } from '@/components/ui/Skeleton'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { getCustomer } from '@/features/customers/api'
import { getInvoice, voidInvoice } from '@/features/invoices/api'
import { DunningCadenceStrip } from '@/features/invoices/DunningCadenceStrip'
import { dunningCallout } from '@/features/invoices/dunningCadence'
import { INVOICE_STATUS_LABEL, invoiceRiskTone } from '@/features/invoices/invoiceStatusTone'
import type { Invoice, InvoiceLineItem, InvoiceLineType } from '@/features/invoices/types'
import { listPaymentAttempts } from '@/features/payments/api'
import { PAYMENT_OUTCOME_LABEL, PAYMENT_OUTCOME_TONE } from '@/features/payments/paymentOutcomeTone'
import { TestCardSelector } from '@/features/payments/TestCardSelector'
import { ApiError } from '@/lib/api/client'
import { formatCents } from '@/lib/money'
import { useNow } from '@/lib/useNow'

const dateFormatter = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium' })
const dateTimeFormatter = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeStyle: 'short' })

const LINE_TYPE_LABEL: Record<Exclude<InvoiceLineType, 'base'>, string> = {
  proration_debit: 'Proration debit',
  proration_credit: 'Proration credit',
  credit_note: 'Credit note',
}

export function InvoiceDetailPage() {
  const { id } = useParams<{ id: string }>()

  const {
    data: invoice,
    isPending,
    isError,
    error,
    refetch,
  } = useQuery({
    queryKey: ['invoices', id],
    queryFn: () => getInvoice(id as string),
    enabled: Boolean(id),
  })

  if (isPending) {
    return (
      <div className="flex flex-col gap-6">
        <CardSkeleton />
        <CardSkeleton />
      </div>
    )
  }

  if (isError || !invoice) {
    return (
      <ErrorState
        message={error instanceof ApiError ? error.message : "Couldn't load this invoice."}
        onRetry={() => refetch()}
      />
    )
  }

  return <InvoiceDetailContent invoice={invoice} />
}

interface InvoiceDetailContentProps {
  invoice: Invoice
}

/**
 * Split from InvoiceDetailPage so the loading/error early returns above stay
 * before any hook that depends on `invoice` actually existing — everything
 * here can assume the invoice is loaded, mirroring SubscriptionDetailPage.
 */
function InvoiceDetailContent({ invoice }: InvoiceDetailContentProps) {
  const queryClient = useQueryClient()
  const now = useNow(60_000)
  const [voidConfirmOpen, setVoidConfirmOpen] = useState(false)

  const {
    data: customer,
    isPending: customerPending,
    isError: customerError,
    refetch: refetchCustomer,
  } = useQuery({
    queryKey: ['customers', invoice.customerId],
    queryFn: () => getCustomer(invoice.customerId),
  })

  const {
    data: attempts,
    isPending: attemptsPending,
    isError: attemptsError,
    refetch: refetchAttempts,
  } = useQuery({
    queryKey: ['payments', 'attempts', invoice.id],
    queryFn: () => listPaymentAttempts(invoice.id),
  })

  const voidMutation = useMutation({
    mutationFn: () => voidInvoice(invoice.id),
    onSuccess: (updated) => {
      queryClient.setQueryData(['invoices', invoice.id], updated)
      queryClient.invalidateQueries({ queryKey: ['invoices'] })
      setVoidConfirmOpen(false)
    },
  })

  const chargeable = invoice.status === 'open'
  const disabledReason =
    invoice.status === 'paid'
      ? 'This invoice is already paid.'
      : invoice.status === 'void'
        ? 'Voided invoices can\'t be charged.'
        : invoice.status === 'uncollectible'
          ? 'This invoice is uncollectible — retries are exhausted.'
          : invoice.status === 'draft'
            ? 'Draft invoices must be issued before they can be charged.'
            : undefined

  const voidable = invoice.status === 'draft' || invoice.status === 'open'
  const callout = dunningCallout(invoice, now)
  // Sorted newest-first for display, regardless of the order the backend returns them in.
  const attemptsNewestFirst = attempts ? [...attempts].reverse() : undefined

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <FileText className="size-6 text-muted-foreground" aria-hidden="true" />
          <h1 className="text-xl font-semibold tracking-tight text-foreground">{invoice.invoiceNumber}</h1>
          <StatusBadge tone={invoiceRiskTone(invoice)}>{INVOICE_STATUS_LABEL[invoice.status]}</StatusBadge>
        </div>
        {voidable && (
          <Button variant="destructive" size="sm" onClick={() => setVoidConfirmOpen(true)}>
            <Ban className="size-4" aria-hidden="true" />
            Void invoice
          </Button>
        )}
      </div>

      {callout && (
        <p
          className={
            invoice.status === 'uncollectible'
              ? 'rounded-md border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700 dark:border-rose-800 dark:bg-rose-950/40 dark:text-rose-300'
              : 'rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800 dark:border-amber-800 dark:bg-amber-950/40 dark:text-amber-300'
          }
        >
          {callout}
        </p>
      )}

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-[1fr_360px]">
        {/* Left pane — the paper-style bill */}
        <Card className="overflow-hidden">
          <CardContent className="flex flex-col gap-6 p-6 sm:p-8">
            <div className="flex flex-col justify-between gap-4 border-b border-border pb-6 sm:flex-row">
              <div className="flex flex-col gap-1">
                <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Invoice</p>
                <p className="text-2xl font-semibold tracking-tight text-foreground">{invoice.invoiceNumber}</p>
              </div>
              <dl className="flex flex-col gap-1 text-sm sm:items-end sm:text-right">
                <div className="flex gap-2 sm:justify-end">
                  <dt className="text-muted-foreground">Issued</dt>
                  <dd className="text-foreground">{dateFormatter.format(new Date(invoice.issueDate))}</dd>
                </div>
                <div className="flex gap-2 sm:justify-end">
                  <dt className="text-muted-foreground">Due</dt>
                  <dd className="text-foreground">{dateFormatter.format(new Date(invoice.dueDate))}</dd>
                </div>
              </dl>
            </div>

            <div className="flex flex-col gap-1">
              <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Bill to</p>
              {customerPending && <p className="text-sm text-muted-foreground">Loading customer…</p>}
              {!customerPending && customerError && (
                <button
                  type="button"
                  onClick={() => refetchCustomer()}
                  className="text-left text-sm text-rose-600 dark:text-rose-400"
                >
                  Couldn't load the customer — retry
                </button>
              )}
              {!customerPending && !customerError && customer && (
                <>
                  <p className="text-sm font-medium text-foreground">{customer.name}</p>
                  <p className="text-sm text-muted-foreground">{customer.email}</p>
                </>
              )}
              {!customerPending && !customerError && !customer && (
                <p className="text-sm text-muted-foreground">{invoice.customerId}</p>
              )}
            </div>

            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="border-b border-border">
                  <tr>
                    <th scope="col" className="py-2 font-medium text-muted-foreground">
                      Description
                    </th>
                    <th scope="col" className="py-2 text-right font-medium text-muted-foreground">
                      Amount
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {invoice.lineItems.map((item: InvoiceLineItem) => {
                    const isCredit = item.amountCents < 0
                    return (
                      <tr key={item.id} className="border-b border-border last:border-0">
                        <td className="py-3 text-foreground">
                          {item.description}
                          {item.lineType !== 'base' && (
                            <span className="ml-2 rounded-full border border-border bg-surface-muted px-2 py-0.5 text-xs text-zinc-600 dark:text-zinc-300">
                              {LINE_TYPE_LABEL[item.lineType]}
                            </span>
                          )}
                        </td>
                        <td
                          className={
                            isCredit
                              ? 'py-3 text-right font-mono tabular-nums text-rose-600 dark:text-rose-400'
                              : 'py-3 text-right font-mono tabular-nums text-foreground'
                          }
                        >
                          {formatCents(item.amountCents, invoice.currency)}
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>

            <div className="flex flex-col items-end gap-1 border-t border-border pt-4">
              <div className="flex w-full max-w-[240px] justify-between text-sm">
                <span className="text-muted-foreground">Subtotal</span>
                <span className="font-mono tabular-nums text-foreground">
                  {formatCents(invoice.subtotalCents, invoice.currency)}
                </span>
              </div>
              <div className="flex w-full max-w-[240px] justify-between text-base font-semibold">
                <span className="text-foreground">Total</span>
                <span className="font-mono tabular-nums text-foreground">{formatCents(invoice.totalCents, invoice.currency)}</span>
              </div>
            </div>

            {invoice.paidAt && (
              <p className="rounded-md border border-border bg-surface-muted px-3 py-2 text-sm text-muted-foreground">
                Paid on {dateFormatter.format(new Date(invoice.paidAt))}
              </p>
            )}
          </CardContent>
        </Card>

        {/* Right pane — actions/status */}
        <div className="flex flex-col gap-6">
          <Card>
            <CardHeader>
              <CardTitle>Dunning status</CardTitle>
            </CardHeader>
            <CardContent className="flex flex-col gap-4">
              <DunningCadenceStrip attemptCount={invoice.dunningAttemptCount} exhausted={invoice.status === 'uncollectible'} />
              <dl className="flex flex-col gap-3 text-sm">
                <div className="flex justify-between">
                  <dt className="text-muted-foreground">Attempts</dt>
                  <dd className="font-mono tabular-nums text-foreground">{invoice.dunningAttemptCount}</dd>
                </div>
                <div className="flex flex-col gap-1">
                  <dt className="text-muted-foreground">Next retry</dt>
                  <dd className="text-foreground">
                    {invoice.nextRetryAt
                      ? dateTimeFormatter.format(new Date(invoice.nextRetryAt))
                      : invoice.status === 'uncollectible'
                        ? 'Retries exhausted — uncollectible.'
                        : invoice.status === 'open' && invoice.dunningAttemptCount === 0
                          ? 'No retry scheduled.'
                          : '—'}
                  </dd>
                </div>
              </dl>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>{chargeable ? 'Charge this invoice' : 'Charge invoice'}</CardTitle>
            </CardHeader>
            <CardContent>
              <TestCardSelector invoiceId={invoice.id} chargeable={chargeable} disabledReason={disabledReason} />
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Payment history</CardTitle>
            </CardHeader>
            <CardContent>
              {attemptsPending && <p className="text-sm text-muted-foreground">Loading payment history…</p>}

              {!attemptsPending && attemptsError && (
                <button
                  type="button"
                  onClick={() => refetchAttempts()}
                  className="text-left text-sm text-rose-600 dark:text-rose-400"
                >
                  Couldn't load payment history — retry
                </button>
              )}

              {!attemptsPending && !attemptsError && attemptsNewestFirst && attemptsNewestFirst.length === 0 && (
                <p className="text-sm text-muted-foreground">No payment attempts have been made against this invoice yet.</p>
              )}

              {!attemptsPending && !attemptsError && attemptsNewestFirst && attemptsNewestFirst.length > 0 && (
                <ul className="flex flex-col gap-3">
                  {attemptsNewestFirst.map((attempt) => (
                    <li key={attempt.id} className="flex flex-col gap-1 border-b border-border pb-3 last:border-0 last:pb-0">
                      <div className="flex items-center justify-between gap-2">
                        <StatusBadge tone={PAYMENT_OUTCOME_TONE[attempt.outcome]}>
                          {PAYMENT_OUTCOME_LABEL[attempt.outcome]}
                        </StatusBadge>
                        <span className="text-xs text-muted-foreground">{dateTimeFormatter.format(new Date(attempt.attemptedAt))}</span>
                      </div>
                      <p className="text-xs text-muted-foreground">Card ending in {attempt.cardNumberLast4}</p>
                    </li>
                  ))}
                </ul>
              )}
            </CardContent>
          </Card>
        </div>
      </div>

      <Modal
        open={voidConfirmOpen}
        onClose={() => setVoidConfirmOpen(false)}
        title="Void this invoice"
        description="This is a real state transition, not a draft edit — it can't be undone from here."
        footer={
          <>
            <Button type="button" variant="secondary" onClick={() => setVoidConfirmOpen(false)} disabled={voidMutation.isPending}>
              Back
            </Button>
            <Button type="button" variant="destructive" onClick={() => voidMutation.mutate()} disabled={voidMutation.isPending}>
              {voidMutation.isPending ? 'Voiding…' : 'Void invoice'}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3 text-sm">
          {voidMutation.isError && (
            <p role="alert" className="text-sm text-rose-600 dark:text-rose-400">
              {voidMutation.error instanceof ApiError ? voidMutation.error.message : "Couldn't void this invoice."}
            </p>
          )}
          <p className="text-foreground">
            Voiding <span className="font-medium">{invoice.invoiceNumber}</span> marks it uncollectable-by-design — it stops
            counting toward what the customer owes and can no longer be charged.
          </p>
        </div>
      </Modal>
    </div>
  )
}
