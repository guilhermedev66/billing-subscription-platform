import { useQuery } from '@tanstack/react-query'
import { FileText } from 'lucide-react'
import { useMemo } from 'react'
import { Link } from 'react-router-dom'
import { EmptyState } from '@/components/ui/EmptyState'
import { ErrorState } from '@/components/ui/ErrorState'
import { CardSkeletonGrid } from '@/components/ui/Skeleton'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { listCustomers } from '@/features/customers/api'
import type { Customer } from '@/features/customers/types'
import { listInvoices } from '@/features/invoices/api'
import { INVOICE_STATUS_LABEL, invoiceRiskTone } from '@/features/invoices/invoiceStatusTone'
import type { Invoice } from '@/features/invoices/types'
import { formatCents } from '@/lib/money'

const dateFormatter = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium' })

export function InvoicesPage() {
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

  const customerById = useMemo(() => {
    const map = new Map<string, Customer>()
    for (const customer of customers ?? []) map.set(customer.id, customer)
    return map
  }, [customers])

  const isPending = invoicesPending || customersPending
  const isError = invoicesError || customersError
  const refetch = () => {
    refetchInvoices()
    refetchCustomers()
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold tracking-tight text-foreground">Invoices</h1>
      </div>

      {isPending && <CardSkeletonGrid count={4} label="Loading invoices" />}

      {isError && <ErrorState message="Couldn't load invoices." onRetry={() => refetch()} />}

      {!isPending && !isError && invoices && invoices.length === 0 && (
        <EmptyState icon={FileText} title="No invoices yet" description="Invoices will appear here once they're issued to customers." />
      )}

      {!isPending && !isError && invoices && invoices.length > 0 && (
        <div className="overflow-x-auto rounded-lg border border-border">
          <table className="w-full text-left text-sm">
            <thead className="border-b border-border bg-surface-muted">
              <tr>
                <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                  Invoice #
                </th>
                <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                  Customer
                </th>
                <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                  Status
                </th>
                <th scope="col" className="px-4 py-2 text-right font-medium text-muted-foreground">
                  Total
                </th>
                <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                  Due date
                </th>
              </tr>
            </thead>
            <tbody>
              {invoices.map((invoice: Invoice) => {
                const customer = customerById.get(invoice.customerId)

                return (
                  <tr
                    key={invoice.id}
                    className="relative border-b border-border last:border-0 hover:bg-surface-muted focus-within:bg-surface-muted"
                  >
                    <td className="px-4 py-3 font-medium text-foreground">
                      <Link to={`/invoices/${invoice.id}`}>
                        <span className="absolute inset-0" aria-hidden="true" />
                        {invoice.invoiceNumber}
                      </Link>
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">{customer?.name ?? invoice.customerId}</td>
                    <td className="px-4 py-3">
                      <StatusBadge tone={invoiceRiskTone(invoice)}>
                        {INVOICE_STATUS_LABEL[invoice.status]}
                        {invoice.status === 'open' && invoice.dunningAttemptCount > 0 && (
                          <span className="sr-only"> — payment failing, attempt {invoice.dunningAttemptCount} of 4</span>
                        )}
                      </StatusBadge>
                    </td>
                    <td className="px-4 py-3 text-right font-mono tabular-nums text-foreground">
                      {formatCents(invoice.totalCents, invoice.currency)}
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">{dateFormatter.format(new Date(invoice.dueDate))}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
