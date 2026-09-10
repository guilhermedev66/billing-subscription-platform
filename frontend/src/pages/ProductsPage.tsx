import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Package, Plus } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link } from 'react-router-dom'
import { Button } from '@/components/ui/Button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { ErrorState } from '@/components/ui/ErrorState'
import { FormField } from '@/components/ui/FormField'
import { Input } from '@/components/ui/Input'
import { CardSkeletonGrid } from '@/components/ui/Skeleton'
import { createProduct, listPrices, listProducts } from '@/features/catalog/api'
import { productSchema, type ProductFormValues } from '@/features/catalog/schemas'
import type { Price, Product } from '@/features/catalog/types'
import { ApiError, splitFieldErrors } from '@/lib/api/client'
import { formatCents } from '@/lib/money'

function formatInterval(interval: Price['interval']) {
  return interval === 'month' ? '/mo' : '/yr'
}

function summarizePrice(price: Price): string {
  switch (price.model) {
    case 'flat':
    case 'per_seat':
      return `${formatCents(price.unitAmountCents ?? 0, price.currency)}${formatInterval(price.interval)}`
    case 'metered':
      return `${formatCents(price.unitAmountCents ?? 0, price.currency)}/unit`
    case 'tiered':
      return `${price.tiers?.length ?? 0}-tier${(price.tiers?.length ?? 0) === 1 ? '' : 's'}`
  }
}

function PriceSummaryCell({ prices }: { prices: Price[] }) {
  if (prices.length === 0) {
    return <span className="text-sm text-muted-foreground">No prices yet</span>
  }

  const [first, ...rest] = prices
  return (
    <span className="font-mono text-sm tabular-nums text-foreground">
      {summarizePrice(first)}
      {rest.length > 0 && <span className="text-muted-foreground"> · +{rest.length} more</span>}
    </span>
  )
}

function NewProductForm({ onDone }: { onDone: () => void }) {
  const queryClient = useQueryClient()
  const [fallbackError, setFallbackError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<ProductFormValues>({
    resolver: zodResolver(productSchema),
    defaultValues: { name: '', description: '' },
  })

  const mutation = useMutation({
    mutationFn: createProduct,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['products'] })
      onDone()
    },
    onError: (error) => {
      if (!(error instanceof ApiError)) {
        setFallbackError('Something went wrong. Please try again.')
        return
      }
      if (error.kind === 'validation' && error.fieldErrors) {
        const { mapped, unmapped } = splitFieldErrors(error.fieldErrors, ['name', 'description'] as const)
        for (const [field, message] of Object.entries(mapped)) {
          setError(field as 'name' | 'description', { message })
        }
        if (unmapped.length > 0) setFallbackError(unmapped.join(' '))
        return
      }
      setFallbackError(error.message)
    },
  })

  const onSubmit = (values: ProductFormValues) => {
    setFallbackError(null)
    mutation.mutate(values)
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>New product</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4" noValidate>
          {fallbackError && (
            <p role="alert" className="text-sm text-rose-600 dark:text-rose-400">
              {fallbackError}
            </p>
          )}
          <FormField id="name" label="Name" error={errors.name?.message}>
            <Input autoFocus {...register('name')} />
          </FormField>
          <FormField id="description" label="Description" error={errors.description?.message} hint="Optional">
            <Input {...register('description')} />
          </FormField>
          <div className="flex gap-2">
            <Button type="submit" disabled={isSubmitting || mutation.isPending}>
              {mutation.isPending ? 'Creating…' : 'Create product'}
            </Button>
            <Button type="button" variant="secondary" onClick={onDone}>
              Cancel
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  )
}

export function ProductsPage() {
  const [isCreating, setIsCreating] = useState(false)

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

  const pricesByProduct = useMemo(() => {
    const map = new Map<string, Price[]>()
    for (const price of prices ?? []) {
      const existing = map.get(price.productId)
      if (existing) existing.push(price)
      else map.set(price.productId, [price])
    }
    return map
  }, [prices])

  const isPending = productsPending || pricesPending
  const isError = productsError || pricesError
  const refetch = () => {
    refetchProducts()
    refetchPrices()
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold tracking-tight text-foreground">Products</h1>
        {!isCreating && (
          <Button size="sm" onClick={() => setIsCreating(true)}>
            <Plus className="size-4" aria-hidden="true" />
            New product
          </Button>
        )}
      </div>

      {isCreating && <NewProductForm onDone={() => setIsCreating(false)} />}

      {isPending && <CardSkeletonGrid count={4} label="Loading products" />}

      {isError && <ErrorState message="Couldn't load products." onRetry={() => refetch()} />}

      {!isPending && !isError && products && products.length === 0 && !isCreating && (
        <EmptyState
          icon={Package}
          title="No products yet"
          description="Create a product, then add its flat, per-seat, tiered, or metered prices."
          action={
            <Button size="sm" onClick={() => setIsCreating(true)}>
              <Plus className="size-4" aria-hidden="true" />
              New product
            </Button>
          }
        />
      )}

      {!isPending && !isError && products && products.length > 0 && (
        <div className="overflow-x-auto rounded-lg border border-border">
          <table className="w-full text-left text-sm">
            <thead className="border-b border-border bg-surface-muted">
              <tr>
                <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                  Name
                </th>
                <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                  Description
                </th>
                <th scope="col" className="px-4 py-2 text-right font-medium text-muted-foreground">
                  Prices
                </th>
              </tr>
            </thead>
            <tbody>
              {products.map((product: Product) => (
                <tr
                  key={product.id}
                  className="relative cursor-pointer border-b border-border last:border-0 hover:bg-surface-muted focus-within:bg-surface-muted"
                >
                  <td className="px-4 py-3 font-medium text-foreground">
                    <Link to={`/products/${product.id}`}>
                      <span className="absolute inset-0" aria-hidden="true" />
                      {product.name}
                    </Link>
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">{product.description || '—'}</td>
                  <td className="px-4 py-3 text-right">
                    <PriceSummaryCell prices={pricesByProduct.get(product.id) ?? []} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
