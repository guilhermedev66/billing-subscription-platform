import { http, HttpResponse } from 'msw'

/**
 * Mirrors the REAL BillingPlatform.Catalog.Api wire contract byte-for-byte —
 * flat /catalog/products and /catalog/prices resources (products don't embed
 * prices), numeric PricingModel/BillingInterval/MeteredAggregation enums (no
 * JsonStringEnumConverter registered server-side), and StartingUnit/EndingUnit
 * tiers — NOT the friendly shape in features/catalog/types.ts. api.ts is what
 * translates between the two for real traffic, and these handlers stand in
 * for that same real traffic in tests, so they have to speak the same wire
 * format it does.
 */
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
  pricingModel: 0 | 1 | 2 | 3
  currency: string
  billingInterval: 0 | 1
  flatUnitAmountCents: number | null
  perSeatUnitAmountCents: number | null
  meteredUnitAmountCents: number | null
  meteredAggregation: 0 | 1 | 2 | null
  tiers: WireTier[]
  trialDays: number | null
}

function problem(status: number, title: string, detail?: string, errors?: Record<string, string[]>) {
  return HttpResponse.json({ status, title, detail, errors }, { status })
}

const orgId = 'org_demo'

const products: WireProduct[] = [
  { id: 'prod_1', organizationId: orgId, name: 'Cloud Analytics Platform', description: 'Real-time dashboards and alerting for product analytics.', active: true },
  { id: 'prod_2', organizationId: orgId, name: 'Team Workspace', description: 'Shared workspace billed per active seat.', active: true },
  { id: 'prod_3', organizationId: orgId, name: 'API Gateway', description: 'Graduated pricing by monthly request volume.', active: true },
  { id: 'prod_4', organizationId: orgId, name: 'Usage Metering Suite', description: 'Billed in arrears from ingested usage events.', active: true },
]

const prices: WirePrice[] = [
  {
    id: 'price_1',
    productId: 'prod_1',
    organizationId: orgId,
    pricingModel: 0,
    currency: 'USD',
    billingInterval: 0,
    flatUnitAmountCents: 2900,
    perSeatUnitAmountCents: null,
    meteredUnitAmountCents: null,
    meteredAggregation: null,
    tiers: [],
    trialDays: null,
  },
  {
    id: 'price_2',
    productId: 'prod_1',
    organizationId: orgId,
    pricingModel: 0,
    currency: 'USD',
    billingInterval: 1,
    flatUnitAmountCents: 29000,
    perSeatUnitAmountCents: null,
    meteredUnitAmountCents: null,
    meteredAggregation: null,
    tiers: [],
    trialDays: null,
  },
  {
    id: 'price_3',
    productId: 'prod_2',
    organizationId: orgId,
    pricingModel: 1,
    currency: 'USD',
    billingInterval: 0,
    flatUnitAmountCents: null,
    perSeatUnitAmountCents: 1200,
    meteredUnitAmountCents: null,
    meteredAggregation: null,
    tiers: [],
    trialDays: null,
  },
  {
    id: 'price_4',
    productId: 'prod_3',
    organizationId: orgId,
    pricingModel: 2,
    currency: 'USD',
    billingInterval: 0,
    flatUnitAmountCents: null,
    perSeatUnitAmountCents: null,
    meteredUnitAmountCents: null,
    meteredAggregation: null,
    tiers: [
      { id: 'tier_1', startingUnit: 1, endingUnit: 5, unitAmountCents: 2000 },
      { id: 'tier_2', startingUnit: 6, endingUnit: 20, unitAmountCents: 1500 },
      { id: 'tier_3', startingUnit: 21, endingUnit: null, unitAmountCents: 1000 },
    ],
    trialDays: null,
  },
  {
    id: 'price_5',
    productId: 'prod_4',
    organizationId: orgId,
    pricingModel: 3,
    currency: 'USD',
    billingInterval: 0,
    flatUnitAmountCents: null,
    perSeatUnitAmountCents: null,
    meteredUnitAmountCents: 5,
    meteredAggregation: 0,
    tiers: [],
    trialDays: null,
  },
  {
    id: 'price_6',
    productId: 'prod_4',
    organizationId: orgId,
    pricingModel: 3,
    currency: 'USD',
    billingInterval: 0,
    flatUnitAmountCents: null,
    perSeatUnitAmountCents: null,
    meteredUnitAmountCents: 3,
    meteredAggregation: 1,
    tiers: [],
    trialDays: null,
  },
]

let nextProductSeq = products.length + 1
let nextPriceSeq = prices.length + 1
let nextTierSeq = prices.reduce((sum, price) => sum + price.tiers.length, 0) + 1

export const catalogHandlers = [
  http.get('/api/catalog/products', () => HttpResponse.json(products)),

  http.get('/api/catalog/products/:id', ({ params }) => {
    const product = products.find((candidate) => candidate.id === params.id)
    if (!product) return problem(404, 'Product not found.')
    return HttpResponse.json(product)
  }),

  http.post('/api/catalog/products', async ({ request }) => {
    const body = (await request.json()) as Partial<{ name: string; description: string; active: boolean }>

    if (!body.name || body.name.trim().length < 2) {
      return problem(400, 'Validation failed.', undefined, { catalog: ['Name must be at least 2 characters.'] })
    }

    const product: WireProduct = {
      id: `prod_${nextProductSeq++}`,
      organizationId: orgId,
      name: body.name,
      description: body.description ?? '',
      active: body.active ?? true,
    }
    products.push(product)
    return HttpResponse.json(product, { status: 201 })
  }),

  http.get('/api/catalog/prices', ({ request }) => {
    const url = new URL(request.url)
    const productId = url.searchParams.get('productId')
    const matching = productId ? prices.filter((price) => price.productId === productId) : prices
    return HttpResponse.json(matching)
  }),

  http.get('/api/catalog/prices/:id', ({ params }) => {
    const price = prices.find((candidate) => candidate.id === params.id)
    if (!price) return problem(404, 'Price not found.')
    return HttpResponse.json(price)
  }),

  http.post('/api/catalog/prices', async ({ request }) => {
    const body = (await request.json()) as Partial<Omit<WirePrice, 'id' | 'organizationId' | 'tiers'>> & {
      tiers?: Omit<WireTier, 'id'>[]
    }

    const product = products.find((candidate) => candidate.id === body.productId)
    if (!product) return problem(404, 'Product not found.')

    if (body.pricingModel === undefined || body.billingInterval === undefined) {
      return problem(400, 'Validation failed.', undefined, {
        catalog: ['Pricing model and billing interval are required.'],
      })
    }

    const price: WirePrice = {
      id: `price_${nextPriceSeq++}`,
      productId: product.id,
      organizationId: orgId,
      pricingModel: body.pricingModel,
      currency: body.currency ?? 'USD',
      billingInterval: body.billingInterval,
      flatUnitAmountCents: body.flatUnitAmountCents ?? null,
      perSeatUnitAmountCents: body.perSeatUnitAmountCents ?? null,
      meteredUnitAmountCents: body.meteredUnitAmountCents ?? null,
      meteredAggregation: body.meteredAggregation ?? null,
      tiers: (body.tiers ?? []).map((tier) => ({ id: `tier_${nextTierSeq++}`, ...tier })),
      trialDays: body.trialDays ?? null,
    }
    prices.push(price)
    return HttpResponse.json(price, { status: 201 })
  }),
]
