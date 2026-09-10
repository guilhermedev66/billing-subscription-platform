import { useQuery } from '@tanstack/react-query'
import { Repeat } from 'lucide-react'
import { useMemo } from 'react'
import { Link } from 'react-router-dom'
import { EmptyState } from '@/components/ui/EmptyState'
import { ErrorState } from '@/components/ui/ErrorState'
import { CardSkeletonGrid } from '@/components/ui/Skeleton'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { listPrices, listProducts } from '@/features/catalog/api'
import type { Price, Product } from '@/features/catalog/types'
import { listCustomers } from '@/features/customers/api'
import type { Customer } from '@/features/customers/types'
import { formatPlanLabel } from '@/features/subscriptions/planLabel'
import { SUBSCRIPTION_STATUS_LABEL, SUBSCRIPTION_STATUS_TONE } from '@/features/subscriptions/statusTone'
import { listSubscriptions } from '@/features/subscriptions/api'
import type { Subscription } from '@/features/subscriptions/types'

const dateFormatter = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium' })

export function SubscriptionsPage() {
  const {
    data: subscriptions,
    isPending: subscriptionsPending,
    isError: subscriptionsError,
    refetch: refetchSubscriptions,
  } = useQuery({ queryKey: ['subscriptions'], queryFn: listSubscriptions })

  const {
    data: customers,
    isPending: customersPending,
    isError: customersError,
    refetch: refetchCustomers,
  } = useQuery({ queryKey: ['customers'], queryFn: listCustomers })

  const {
    data: products,
    isPending: productsPending,
    isError: productsError,
    refetch: refetchProducts,
  } = useQuery({ queryKey: ['products'], queryFn: listProducts })

  const {
    data: prices,
    isPending: pricesPending,
    isError: pricesError,
    refetch: refetchPrices,
  } = useQuery({ queryKey: ['prices'], queryFn: () => listPrices() })

  const customerById = useMemo(() => {
    const map = new Map<string, Customer>()
    for (const customer of customers ?? []) map.set(customer.id, customer)
    return map
  }, [customers])

  const productById = useMemo(() => {
    const map = new Map<string, Product>()
    for (const product of products ?? []) map.set(product.id, product)
    return map
  }, [products])

  const priceById = useMemo(() => {
    const map = new Map<string, Price>()
    for (const price of prices ?? []) map.set(price.id, price)
    return map
  }, [prices])

  const isPending = subscriptionsPending || customersPending || productsPending || pricesPending
  const isError = subscriptionsError || customersError || productsError || pricesError
  const refetch = () => {
    refetchSubscriptions()
    refetchCustomers()
    refetchProducts()
    refetchPrices()
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold tracking-tight text-foreground">Subscriptions</h1>
      </div>

      {isPending && <CardSkeletonGrid count={4} label="Loading subscriptions" />}

      {isError && <ErrorState message="Couldn't load subscriptions." onRetry={() => refetch()} />}

      {!isPending && !isError && subscriptions && subscriptions.length === 0 && (
        <EmptyState icon={Repeat} title="No subscriptions yet" description="Subscriptions will appear here once customers subscribe to a plan." />
      )}

      {!isPending && !isError && subscriptions && subscriptions.length > 0 && (
        <div className="overflow-x-auto rounded-lg border border-border">
          <table className="w-full text-left text-sm">
            <thead className="border-b border-border bg-surface-muted">
              <tr>
                <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                  Customer
                </th>
                <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                  Plan
                </th>
                <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                  Status
                </th>
                <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                  Current period ends
                </th>
                <th scope="col" className="px-4 py-2 text-right font-medium text-muted-foreground">
                  Seats
                </th>
              </tr>
            </thead>
            <tbody>
              {subscriptions.map((subscription: Subscription) => {
                const customer = customerById.get(subscription.customerId)
                const price = priceById.get(subscription.priceId)
                const product = price ? productById.get(price.productId) : undefined

                return (
                  <tr
                    key={subscription.id}
                    className="relative border-b border-border last:border-0 hover:bg-surface-muted focus-within:bg-surface-muted"
                  >
                    <td className="px-4 py-3 font-medium text-foreground">
                      <Link to={`/subscriptions/${subscription.id}`}>
                        <span className="absolute inset-0" aria-hidden="true" />
                        {customer?.name ?? subscription.customerId}
                      </Link>
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">{formatPlanLabel(product, price)}</td>
                    <td className="px-4 py-3">
                      <StatusBadge tone={SUBSCRIPTION_STATUS_TONE[subscription.status]}>
                        {SUBSCRIPTION_STATUS_LABEL[subscription.status]}
                      </StatusBadge>
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">
                      {dateFormatter.format(new Date(subscription.currentPeriodEnd))}
                    </td>
                    <td className="px-4 py-3 text-right font-mono tabular-nums text-foreground">
                      {subscription.seatCount ?? '—'}
                    </td>
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
