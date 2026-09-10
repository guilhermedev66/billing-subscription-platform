import type { Price, Product } from '@/features/catalog/types'
import { formatCents } from '@/lib/money'

function intervalSuffix(price: Price): string {
  if (price.model === 'per_seat') return price.interval === 'month' ? '/seat/mo' : '/seat/yr'
  return price.interval === 'month' ? '/mo' : '/yr'
}

/** "Product name — $29.00/mo" — the join between a Subscription's priceId and the catalog data it doesn't embed. */
export function formatPlanLabel(product: Product | undefined, price: Price | undefined): string {
  if (!product || !price) return 'Unknown plan'
  return `${product.name} — ${formatCents(price.unitAmountCents ?? 0, price.currency)}${intervalSuffix(price)}`
}
