import { api } from '@/lib/api/client'
import type {
  AggregationType,
  BillingInterval,
  CreatePriceInput,
  CreateProductInput,
  Price,
  PriceTier,
  PricingModel,
  Product,
} from './types'

/**
 * Catalog module endpoints, aligned to the real BillingPlatform.Catalog.Api
 * contract (CatalogEndpoints.cs / CatalogContracts.cs): flat `/catalog/products`
 * and `/catalog/prices` siblings (products don't embed prices — list them
 * separately, optionally filtered by productId), price creation takes
 * `productId` in the body, and PricingModel/BillingInterval/MeteredAggregation
 * are C# enums with no JsonStringEnumConverter registered server-side, so they
 * serialize as their numeric ordinal — never as strings.
 *
 * This file is the ONLY place that numeric-enum / StartingUnit-EndingUnit wire
 * shape exists; everywhere else in the app works with the friendly string/upTo
 * shape from ./types, the same way lib/money.ts is the only place cents become
 * a display string.
 */

type WirePricingModel = 0 | 1 | 2 | 3
type WireBillingInterval = 0 | 1
type WireAggregation = 0 | 1 | 2

const PRICING_MODEL_TO_WIRE: Record<PricingModel, WirePricingModel> = {
  flat: 0,
  per_seat: 1,
  tiered: 2,
  metered: 3,
}

const PRICING_MODEL_FROM_WIRE: Record<WirePricingModel, PricingModel> = {
  0: 'flat',
  1: 'per_seat',
  2: 'tiered',
  3: 'metered',
}

const INTERVAL_TO_WIRE: Record<BillingInterval, WireBillingInterval> = { month: 0, year: 1 }
const INTERVAL_FROM_WIRE: Record<WireBillingInterval, BillingInterval> = { 0: 'month', 1: 'year' }

const AGGREGATION_TO_WIRE: Record<AggregationType, WireAggregation> = { sum: 0, max: 1, last: 2 }
const AGGREGATION_FROM_WIRE: Record<WireAggregation, AggregationType> = { 0: 'sum', 1: 'max', 2: 'last' }

interface WireTier {
  id: string
  startingUnit: number
  endingUnit: number | null
  unitAmountCents: number
}

interface WireProduct {
  id: string
  organizationId: string
  name: string
  description: string
  active: boolean
}

interface WirePrice {
  id: string
  productId: string
  organizationId: string
  pricingModel: WirePricingModel
  currency: string
  billingInterval: WireBillingInterval
  flatUnitAmountCents: number | null
  perSeatUnitAmountCents: number | null
  meteredUnitAmountCents: number | null
  meteredAggregation: WireAggregation | null
  tiers: WireTier[]
  trialDays: number | null
}

function productFromWire(wire: WireProduct): Product {
  return { id: wire.id, name: wire.name, description: wire.description, active: wire.active }
}

function tiersFromWire(tiers: WireTier[]): PriceTier[] {
  return tiers.map((tier) => ({ upTo: tier.endingUnit, unitAmountCents: tier.unitAmountCents }))
}

function tiersToWire(tiers: PriceTier[]): Omit<WireTier, 'id'>[] {
  let nextStartingUnit = 1
  return tiers.map((tier) => {
    const startingUnit = nextStartingUnit
    if (tier.upTo !== null) nextStartingUnit = tier.upTo + 1
    return { startingUnit, endingUnit: tier.upTo, unitAmountCents: tier.unitAmountCents }
  })
}

function priceFromWire(wire: WirePrice): Price {
  const model = PRICING_MODEL_FROM_WIRE[wire.pricingModel]
  const unitAmountCents = wire.flatUnitAmountCents ?? wire.perSeatUnitAmountCents ?? wire.meteredUnitAmountCents

  return {
    id: wire.id,
    productId: wire.productId,
    model,
    interval: INTERVAL_FROM_WIRE[wire.billingInterval],
    currency: wire.currency,
    unitAmountCents: unitAmountCents ?? undefined,
    tiers: model === 'tiered' ? tiersFromWire(wire.tiers) : undefined,
    aggregation: wire.meteredAggregation === null ? undefined : AGGREGATION_FROM_WIRE[wire.meteredAggregation],
    trialDays: wire.trialDays,
  }
}

function priceToWireRequest(input: CreatePriceInput) {
  return {
    productId: input.productId,
    pricingModel: PRICING_MODEL_TO_WIRE[input.model],
    currency: input.currency,
    billingInterval: INTERVAL_TO_WIRE[input.interval],
    flatUnitAmountCents: input.model === 'flat' ? (input.unitAmountCents ?? null) : null,
    perSeatUnitAmountCents: input.model === 'per_seat' ? (input.unitAmountCents ?? null) : null,
    meteredUnitAmountCents: input.model === 'metered' ? (input.unitAmountCents ?? null) : null,
    meteredAggregation: input.model === 'metered' && input.aggregation ? AGGREGATION_TO_WIRE[input.aggregation] : null,
    tiers: input.model === 'tiered' ? tiersToWire(input.tiers ?? []) : [],
    trialDays: input.trialDays ?? null,
  }
}

export async function listProducts(): Promise<Product[]> {
  const wireProducts = await api.get<WireProduct[]>('/catalog/products')
  return wireProducts.map(productFromWire)
}

export async function getProduct(id: string): Promise<Product> {
  const wireProduct = await api.get<WireProduct>(`/catalog/products/${id}`)
  return productFromWire(wireProduct)
}

export async function createProduct(input: CreateProductInput): Promise<Product> {
  const wireProduct = await api.post<WireProduct>('/catalog/products', input)
  return productFromWire(wireProduct)
}

/** Omit `productId` to list every price across the organization's whole catalog. */
export async function listPrices(productId?: string): Promise<Price[]> {
  const query = productId ? `?${new URLSearchParams({ productId }).toString()}` : ''
  const wirePrices = await api.get<WirePrice[]>(`/catalog/prices${query}`)
  return wirePrices.map(priceFromWire)
}

export async function createPrice(input: CreatePriceInput): Promise<Price> {
  const wirePrice = await api.post<WirePrice>('/catalog/prices', priceToWireRequest(input))
  return priceFromWire(wirePrice)
}
