import { useQuery } from '@tanstack/react-query'
import { ChevronDown, ChevronUp, Package } from 'lucide-react'
import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/Card'
import { ErrorState } from '@/components/ui/ErrorState'
import { CardSkeleton } from '@/components/ui/Skeleton'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { getProduct, listPrices } from '@/features/catalog/api'
import { PriceBuilder } from '@/features/catalog/PriceBuilder'
import type { AggregationType, Price, PricingModel } from '@/features/catalog/types'
import { formatCents } from '@/lib/money'

const MODEL_LABELS: Record<PricingModel, string> = {
  flat: 'Flat fee',
  per_seat: 'Per seat',
  tiered: 'Tiered',
  metered: 'Metered',
}

const AGGREGATION_LABELS: Record<AggregationType, string> = {
  sum: 'Sum',
  max: 'Max',
  last: 'Last',
}

function PriceAmount({ price }: { price: Price }) {
  if (price.model === 'tiered') {
    return (
      <div className="flex flex-col items-end gap-0.5 font-mono text-sm tabular-nums text-foreground">
        {(price.tiers ?? []).map((tier, index) => (
          <span key={index}>
            {tier.upTo === null ? 'above' : `up to ${tier.upTo}`}: {formatCents(tier.unitAmountCents, price.currency)}
          </span>
        ))}
      </div>
    )
  }

  const suffix = price.model === 'metered' ? '/unit' : price.interval === 'month' ? '/mo' : '/yr'
  return (
    <span className="font-mono text-sm tabular-nums text-foreground">
      {formatCents(price.unitAmountCents ?? 0, price.currency)}
      {suffix}
    </span>
  )
}

function PriceRow({ price }: { price: Price }) {
  return (
    <tr className="border-b border-border last:border-0">
      <td className="px-4 py-3 text-foreground">{MODEL_LABELS[price.model]}</td>
      <td className="px-4 py-3 text-muted-foreground">
        {price.model === 'metered' ? AGGREGATION_LABELS[price.aggregation ?? 'sum'] : price.interval === 'month' ? 'Monthly' : 'Yearly'}
      </td>
      <td className="px-4 py-3 text-right">
        <PriceAmount price={price} />
      </td>
    </tr>
  )
}

export function ProductDetailPage() {
  const { id } = useParams<{ id: string }>()
  const [isAddingPrice, setIsAddingPrice] = useState(false)

  const {
    data: product,
    isPending: productPending,
    isError: productError,
    refetch: refetchProduct,
  } = useQuery({
    queryKey: ['products', id],
    queryFn: () => getProduct(id as string),
    enabled: Boolean(id),
  })

  const {
    data: prices,
    isPending: pricesPending,
    isError: pricesError,
    refetch: refetchPrices,
  } = useQuery({
    queryKey: ['prices', id],
    queryFn: () => listPrices(id as string),
    enabled: Boolean(id),
  })

  if (productPending || pricesPending) {
    return (
      <div className="flex flex-col gap-6">
        <CardSkeleton />
        <CardSkeleton />
      </div>
    )
  }

  if (productError || pricesError || !product || !prices) {
    return (
      <ErrorState
        message="Couldn't load this product."
        onRetry={() => {
          refetchProduct()
          refetchPrices()
        }}
      />
    )
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-3">
        <Package className="size-6 text-muted-foreground" aria-hidden="true" />
        <div className="flex flex-col gap-1">
          <div className="flex items-center gap-2">
            <h1 className="text-xl font-semibold tracking-tight text-foreground">{product.name}</h1>
            <StatusBadge tone={product.active ? 'success' : 'neutral'}>
              {product.active ? 'Active' : 'Inactive'}
            </StatusBadge>
          </div>
          {product.description && <p className="text-sm text-muted-foreground">{product.description}</p>}
        </div>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Prices</CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          {prices.length === 0 ? (
            <p className="px-4 py-6 text-sm text-muted-foreground">No prices yet — add one below.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="border-b border-border bg-surface-muted">
                  <tr>
                    <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                      Model
                    </th>
                    <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                      Interval / aggregation
                    </th>
                    <th scope="col" className="px-4 py-2 text-right font-medium text-muted-foreground">
                      Amount
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {prices.map((price) => (
                    <PriceRow key={price.id} price={price} />
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <button
            type="button"
            onClick={() => setIsAddingPrice((open) => !open)}
            aria-expanded={isAddingPrice}
            className="flex w-full items-center justify-between text-left"
          >
            <CardTitle>New price</CardTitle>
            {isAddingPrice ? (
              <ChevronUp className="size-4 text-muted-foreground" aria-hidden="true" />
            ) : (
              <ChevronDown className="size-4 text-muted-foreground" aria-hidden="true" />
            )}
          </button>
        </CardHeader>
        {isAddingPrice && (
          <CardContent>
            <PriceBuilder productId={product.id} onCreated={() => setIsAddingPrice(false)} />
          </CardContent>
        )}
      </Card>
    </div>
  )
}
