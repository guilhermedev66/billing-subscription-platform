export type PricingModel = 'flat' | 'per_seat' | 'tiered' | 'metered'

export type BillingInterval = 'month' | 'year'

export type AggregationType = 'sum' | 'max' | 'last'

/**
 * `upTo === null` marks the open-ended last tier — no other tier may use it.
 * The UI never asks for a starting unit: it's always the previous tier's
 * `upTo` + 1 (unit 1 for the first tier), which is exactly the contiguous,
 * non-overlapping shape BillingPlatform.Catalog.Domain.Price enforces
 * server-side via StartingUnit/EndingUnit — see features/catalog/api.ts for
 * that conversion at the wire boundary.
 */
export interface PriceTier {
  upTo: number | null
  unitAmountCents: number
}

/**
 * UI-facing Price shape. The real BillingPlatform.Catalog.Api contract
 * (CatalogContracts.cs PriceSummary) has no `active`/`createdAt` on Price and
 * splits the amount across three separate nullable fields plus a numeric
 * enum for the pricing model — all of that wire detail is confined to
 * features/catalog/api.ts, which maps to/from this shape.
 */
export interface Price {
  id: string
  productId: string
  model: PricingModel
  interval: BillingInterval
  currency: string
  /** flat / per_seat: the recurring amount. metered: the per-unit amount. Unused by tiered. */
  unitAmountCents?: number
  /** tiered only. */
  tiers?: PriceTier[]
  /** metered only. */
  aggregation?: AggregationType
  /** Free-trial length in days, if any — not yet exposed in the price builder UI. */
  trialDays?: number | null
}

/** Products don't embed their prices server-side — fetch them separately via listPrices(). */
export interface Product {
  id: string
  name: string
  description: string
  active: boolean
}

export interface CreateProductInput {
  name: string
  description?: string
}

export interface CreatePriceInput {
  productId: string
  model: PricingModel
  interval: BillingInterval
  currency: string
  unitAmountCents?: number
  tiers?: PriceTier[]
  aggregation?: AggregationType
  trialDays?: number | null
}
