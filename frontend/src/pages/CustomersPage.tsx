import { useState } from 'react'
import { Plus, Users } from 'lucide-react'
import { useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { EmptyState } from '@/components/ui/EmptyState'
import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { CustomerSheet } from '@/features/customers/CustomerSheet'
import { listCustomers } from '@/features/customers/api'
import type { Customer } from '@/features/customers/types'
import { ApiError } from '@/lib/api/client'
import { formatBalanceCents } from '@/lib/money'

const dateFormatter = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium' })

type SheetState = { customer: Customer | null } | null

export function CustomersPage() {
  const [sheet, setSheet] = useState<SheetState>(null)

  const {
    data: customers,
    isPending,
    isError,
    error,
    refetch,
  } = useQuery({ queryKey: ['customers'], queryFn: listCustomers })

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold tracking-tight text-foreground">Customers</h1>
        <Button onClick={() => setSheet({ customer: null })}>
          <Plus className="size-4" aria-hidden="true" />
          New customer
        </Button>
      </div>

      {isPending && <CustomersTableSkeleton />}

      {isError && (
        <ErrorState
          message={error instanceof ApiError ? error.message : 'Something went wrong loading customers.'}
          onRetry={() => refetch()}
        />
      )}

      {customers && customers.length === 0 && (
        <EmptyState
          icon={Users}
          title="No customers yet"
          description="Create your first customer to start billing them."
          action={<Button onClick={() => setSheet({ customer: null })}>New customer</Button>}
        />
      )}

      {customers && customers.length > 0 && (
        <div className="overflow-x-auto rounded-lg border border-border">
          <table className="w-full text-left text-sm">
            <thead className="border-b border-border bg-surface-muted">
              <tr>
                <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                  Name
                </th>
                <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                  Email
                </th>
                <th scope="col" className="px-4 py-2 text-right font-medium text-muted-foreground">
                  Balance
                </th>
                <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                  Status
                </th>
                <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                  Created
                </th>
              </tr>
            </thead>
            <tbody>
              {customers.map((customer) => (
                <tr
                  key={customer.id}
                  className="relative cursor-pointer border-b border-border last:border-0 hover:bg-surface-muted focus-within:bg-surface-muted"
                >
                  <td className="px-4 py-3 font-medium text-foreground">
                    <button type="button" onClick={() => setSheet({ customer })} className="text-left">
                      <span className="absolute inset-0" aria-hidden="true" />
                      {customer.name}
                    </button>
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">{customer.email}</td>
                  <td className="px-4 py-3 text-right font-mono tabular-nums text-foreground">
                    {formatBalanceCents(customer.balanceCents)}
                  </td>
                  <td className="px-4 py-3">
                    <StatusBadge tone={customer.delinquentFlag ? 'destructive' : 'success'}>
                      {customer.delinquentFlag ? 'Delinquent' : 'Good standing'}
                    </StatusBadge>
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">
                    {dateFormatter.format(new Date(customer.createdAt))}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <CustomerSheet open={sheet !== null} onClose={() => setSheet(null)} customer={sheet?.customer ?? null} />
    </div>
  )
}

function CustomersTableSkeleton() {
  return (
    <div role="status" aria-live="polite" aria-label="Loading customers" className="flex flex-col gap-2">
      {Array.from({ length: 5 }, (_, i) => (
        <Skeleton key={i} className="h-10 w-full" />
      ))}
    </div>
  )
}
