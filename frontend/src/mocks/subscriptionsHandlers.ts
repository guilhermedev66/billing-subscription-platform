import { http, HttpResponse } from 'msw'
import type { ChangePreviewRequest, ProrationResult } from '@/features/subscriptions/types'

/**
 * Mirrors the REAL BillingPlatform.Subscriptions.Api wire contract —
 * numeric SubscriptionStatus (no JsonStringEnumConverter registered
 * server-side), `seatCount`/`canceledAt`/`version` fields, `/preview-proration`
 * (not `/preview-change`), a `{ subscription, proration }` receipt from both
 * preview-proration and apply-change, and a body-less immediate-only
 * `/cancel` — NOT the friendly shape in features/subscriptions/types.ts.
 * api.ts is what translates between the two for real traffic, and these
 * handlers stand in for that same real traffic in tests, so they have to
 * speak the same wire format it does.
 *
 * The proration math replicates BillingPlatform.Subscriptions.Application's
 * ProrationCalculator: remaining time clamps to the full period when "now" is
 * at or before periodStart, and to zero when "now" is at or after periodEnd
 * (docs/research/M0-Billing-Platform-Research-Brief.md §2.4 boundary rules).
 */
const DAY_MS = 24 * 60 * 60 * 1000

type WireSubscriptionStatus = 0 | 1 | 2 | 3 | 4 | 5

const TRIALING: WireSubscriptionStatus = 0
const ACTIVE: WireSubscriptionStatus = 1
const PAST_DUE: WireSubscriptionStatus = 2
const UNPAID: WireSubscriptionStatus = 3
const CANCELED: WireSubscriptionStatus = 4
const PAUSED: WireSubscriptionStatus = 5

interface WireSubscription {
  id: string
  organizationId: string
  customerId: string
  priceId: string
  status: WireSubscriptionStatus
  currentPeriodStart: string
  currentPeriodEnd: string
  trialEnd: string | null
  seatCount: number | null
  canceledAt: string | null
  createdAt: string
  version: number
}

interface MockPrice {
  id: string
  model: 'flat' | 'per_seat'
  unitAmountCents: number
  currency: string
}

/**
 * Only price_1 (flat, prod_1, $29/mo), price_2 (flat, prod_1, $290/yr) and
 * price_3 (per_seat, prod_2, $12/seat/mo) are used by seeded subscriptions and
 * accepted as change targets — tiered/metered subscription proration remains
 * a deliberate M3 scope cut. This duplicates a handful of amounts from
 * catalogHandlers.ts's seed data rather than importing it, so the two mock
 * modules stay independent of each other's internal wire shape.
 */
const PRICES: Record<string, MockPrice> = {
  price_1: { id: 'price_1', model: 'flat', unitAmountCents: 2900, currency: 'USD' },
  price_2: { id: 'price_2', model: 'flat', unitAmountCents: 29000, currency: 'USD' },
  price_3: { id: 'price_3', model: 'per_seat', unitAmountCents: 1200, currency: 'USD' },
}

function isoAtOffset(days: number): string {
  return new Date(Date.now() + days * DAY_MS).toISOString()
}

function problem(status: number, title: string, detail?: string, errors?: Record<string, string[]>) {
  return HttpResponse.json({ status, title, detail, errors }, { status })
}

const seedSubscriptions: WireSubscription[] = [
  {
    id: 'sub_1',
    organizationId: 'org_demo',
    customerId: 'cus_wayne',
    priceId: 'price_1',
    status: ACTIVE,
    currentPeriodStart: isoAtOffset(-10),
    currentPeriodEnd: isoAtOffset(20),
    trialEnd: null,
    seatCount: null,
    canceledAt: null,
    createdAt: isoAtOffset(-70),
    version: 1,
  },
  {
    id: 'sub_2',
    organizationId: 'org_demo',
    customerId: 'cus_stark',
    priceId: 'price_3',
    status: ACTIVE,
    currentPeriodStart: isoAtOffset(-15),
    currentPeriodEnd: isoAtOffset(15),
    trialEnd: null,
    seatCount: 5,
    canceledAt: null,
    createdAt: isoAtOffset(-45),
    version: 1,
  },
  {
    id: 'sub_3',
    organizationId: 'org_demo',
    customerId: 'cus_globex',
    priceId: 'price_2',
    status: TRIALING,
    currentPeriodStart: isoAtOffset(-5),
    currentPeriodEnd: isoAtOffset(360),
    trialEnd: isoAtOffset(9),
    seatCount: null,
    canceledAt: null,
    createdAt: isoAtOffset(-5),
    version: 1,
  },
  {
    id: 'sub_4',
    organizationId: 'org_demo',
    customerId: 'cus_initech',
    priceId: 'price_1',
    status: PAST_DUE,
    currentPeriodStart: isoAtOffset(-25),
    currentPeriodEnd: isoAtOffset(5),
    trialEnd: null,
    seatCount: null,
    canceledAt: null,
    createdAt: isoAtOffset(-115),
    version: 2,
  },
  {
    id: 'sub_5',
    organizationId: 'org_demo',
    customerId: 'cus_umbrella',
    priceId: 'price_3',
    status: UNPAID,
    currentPeriodStart: isoAtOffset(-40),
    currentPeriodEnd: isoAtOffset(-10),
    trialEnd: null,
    seatCount: 3,
    canceledAt: null,
    createdAt: isoAtOffset(-160),
    version: 3,
  },
  {
    id: 'sub_6',
    organizationId: 'org_demo',
    customerId: 'cus_aperture',
    priceId: 'price_1',
    status: CANCELED,
    currentPeriodStart: isoAtOffset(-55),
    currentPeriodEnd: isoAtOffset(-25),
    trialEnd: null,
    seatCount: null,
    canceledAt: isoAtOffset(-26),
    createdAt: isoAtOffset(-200),
    version: 2,
  },
  {
    id: 'sub_7',
    organizationId: 'org_demo',
    customerId: 'cus_soylent',
    priceId: 'price_3',
    status: PAUSED,
    currentPeriodStart: isoAtOffset(-8),
    currentPeriodEnd: isoAtOffset(22),
    trialEnd: null,
    seatCount: 8,
    canceledAt: null,
    createdAt: isoAtOffset(-95),
    version: 2,
  },
  {
    id: 'sub_8',
    organizationId: 'org_demo',
    customerId: 'cus_hooli',
    priceId: 'price_1',
    status: ACTIVE,
    currentPeriodStart: isoAtOffset(-21),
    currentPeriodEnd: isoAtOffset(9),
    trialEnd: null,
    seatCount: null,
    canceledAt: null,
    createdAt: isoAtOffset(-380),
    version: 1,
  },
]

const subscriptions = new Map<string, WireSubscription>(seedSubscriptions.map((subscription) => [subscription.id, subscription]))

interface ResolvedTarget {
  priceId: string
  price: MockPrice
  seatCount: number | null
}

/** Shared by preview-proration and apply-change so both agree on what "the target price/seat count" means for a request. */
function resolveChangeTarget(subscription: WireSubscription, body: ChangePreviewRequest): ResolvedTarget | Response {
  if (body.newPriceId === undefined && body.newSeatCount === undefined) {
    return problem(400, 'Validation failed.', undefined, {
      subscription: ['Provide newPriceId or newSeatCount.'],
    })
  }

  const priceId = body.newPriceId ?? subscription.priceId
  const price = PRICES[priceId]
  if (!price) return problem(404, 'Price not found.')

  if (body.newSeatCount !== undefined && price.model !== 'per_seat') {
    return problem(400, 'Validation failed.', undefined, {
      newSeatCount: ['Seat count only applies to per-seat prices.'],
    })
  }

  if (body.newSeatCount !== undefined && (!Number.isInteger(body.newSeatCount) || body.newSeatCount <= 0)) {
    return problem(400, 'Validation failed.', undefined, {
      newSeatCount: ['Seat count must be a positive whole number.'],
    })
  }

  const seatCount = price.model === 'per_seat' ? (body.newSeatCount ?? subscription.seatCount ?? 1) : null
  return { priceId, price, seatCount }
}

function recurringAmountFor(price: MockPrice, seatCount: number | null): number {
  return price.model === 'per_seat' ? price.unitAmountCents * (seatCount ?? 1) : price.unitAmountCents
}

function computeProration(subscription: WireSubscription, currentPrice: MockPrice, target: ResolvedTarget): ProrationResult {
  // now stands in for IVirtualClock until the Simulation Clock lands (M5) — see docs/ARCHITECTURE.md invariant #7.
  const now = Date.now()
  const periodStart = new Date(subscription.currentPeriodStart).getTime()
  const periodEnd = new Date(subscription.currentPeriodEnd).getTime()
  const durationMs = periodEnd - periodStart

  // Clamp to the full period before it starts, and to zero once it's over — never negative or over 100%.
  let remainingMs: number
  if (now <= periodStart) remainingMs = durationMs
  else if (now >= periodEnd) remainingMs = 0
  else remainingMs = periodEnd - now

  const currentRecurringCents = recurringAmountFor(currentPrice, subscription.seatCount)
  const newRecurringCents = recurringAmountFor(target.price, target.seatCount)

  const creditMagnitude = Math.round((currentRecurringCents * remainingMs) / durationMs)
  const proratedChargeCents = Math.round((newRecurringCents * remainingMs) / durationMs)
  const proratedCreditCents = -creditMagnitude

  const remainingCycleDays = Math.ceil(remainingMs / DAY_MS)
  const remainingCyclePercentage = Math.round(((remainingMs * 100) / durationMs) * 100) / 100

  return {
    currentPlan: { priceId: subscription.priceId, currency: currentPrice.currency, recurringAmountCents: currentRecurringCents },
    newPlan: { priceId: target.priceId, currency: target.price.currency, recurringAmountCents: newRecurringCents },
    remainingCycleDays,
    remainingCyclePercentage,
    proratedCreditCents,
    proratedChargeCents,
    amountDueImmediatelyCents: proratedCreditCents + proratedChargeCents,
    nextRegularRenewalAmountCents: newRecurringCents,
  }
}

export const subscriptionsHandlers = [
  http.get('/api/subscriptions', () => HttpResponse.json(Array.from(subscriptions.values()))),

  http.get('/api/subscriptions/:id', ({ params }) => {
    const subscription = subscriptions.get(params.id as string)
    if (!subscription) return problem(404, 'Subscription not found.')
    return HttpResponse.json(subscription)
  }),

  http.post('/api/subscriptions/:id/preview-proration', async ({ params, request }) => {
    const subscription = subscriptions.get(params.id as string)
    if (!subscription) return problem(404, 'Subscription not found.')

    const currentPrice = PRICES[subscription.priceId]
    if (!currentPrice) return problem(404, 'Price not found.')

    const body = (await request.json()) as ChangePreviewRequest
    const target = resolveChangeTarget(subscription, body)
    if (target instanceof Response) return target

    return HttpResponse.json({ subscription, proration: computeProration(subscription, currentPrice, target) })
  }),

  http.post('/api/subscriptions/:id/apply-change', async ({ params, request }) => {
    const id = params.id as string
    const subscription = subscriptions.get(id)
    if (!subscription) return problem(404, 'Subscription not found.')

    if (subscription.status === CANCELED || subscription.status === PAUSED) {
      return problem(409, 'The subscription was modified by another request.', 'Cannot change a canceled or paused subscription.')
    }

    const currentPrice = PRICES[subscription.priceId]
    if (!currentPrice) return problem(404, 'Price not found.')

    const body = (await request.json()) as ChangePreviewRequest
    const target = resolveChangeTarget(subscription, body)
    if (target instanceof Response) return target

    const proration = computeProration(subscription, currentPrice, target)

    // No invoice is created here — the Billing/invoicing module doesn't exist until M4.
    const updated: WireSubscription = {
      ...subscription,
      priceId: target.priceId,
      seatCount: target.seatCount,
      version: subscription.version + 1,
    }
    subscriptions.set(id, updated)
    return HttpResponse.json({ subscription: updated, proration })
  }),

  http.post('/api/subscriptions/:id/pause', ({ params }) => {
    const id = params.id as string
    const subscription = subscriptions.get(id)
    if (!subscription) return problem(404, 'Subscription not found.')
    if (subscription.status === CANCELED || subscription.status === PAUSED) {
      return problem(409, 'The subscription was modified by another request.', 'This subscription cannot be paused.')
    }

    const updated: WireSubscription = { ...subscription, status: PAUSED, version: subscription.version + 1 }
    subscriptions.set(id, updated)
    return HttpResponse.json(updated)
  }),

  http.post('/api/subscriptions/:id/resume', ({ params }) => {
    const id = params.id as string
    const subscription = subscriptions.get(id)
    if (!subscription) return problem(404, 'Subscription not found.')
    if (subscription.status !== PAUSED) {
      return problem(409, 'The subscription was modified by another request.', 'Only a paused subscription can be resumed.')
    }

    const updated: WireSubscription = { ...subscription, status: ACTIVE, version: subscription.version + 1 }
    subscriptions.set(id, updated)
    return HttpResponse.json(updated)
  }),

  http.post('/api/subscriptions/:id/cancel', ({ params }) => {
    const id = params.id as string
    const subscription = subscriptions.get(id)
    if (!subscription) return problem(404, 'Subscription not found.')
    if (subscription.status === CANCELED) {
      return problem(409, 'The subscription was modified by another request.', 'This subscription is already canceled.')
    }

    const updated: WireSubscription = {
      ...subscription,
      status: CANCELED,
      canceledAt: new Date().toISOString(),
      version: subscription.version + 1,
    }
    subscriptions.set(id, updated)
    return HttpResponse.json(updated)
  }),
]

/**
 * Cross-module mock effect for mocks/paymentsHandlers.ts: a payment attempt
 * recovers or degrades a subscription's status the same way the real
 * Payments module would call into Subscriptions' application layer (never
 * touching another module's row directly — this is the mock's equivalent of
 * that call, not a shortcut around module boundaries).
 */
export function setSubscriptionStatusForPayment(
  subscriptionId: string,
  status: WireSubscriptionStatus,
): WireSubscription | undefined {
  const subscription = subscriptions.get(subscriptionId)
  if (!subscription) return undefined

  const updated: WireSubscription = { ...subscription, status, version: subscription.version + 1 }
  subscriptions.set(subscriptionId, updated)
  return updated
}

export const SUBSCRIPTION_STATUS_WIRE = { TRIALING, ACTIVE, PAST_DUE, UNPAID, CANCELED, PAUSED }
