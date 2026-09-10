export type SubscriptionStatus = 'trialing' | 'active' | 'past_due' | 'unpaid' | 'canceled' | 'paused'

/**
 * UI-facing Subscription shape. Field names/casing mirror
 * BillingPlatform.Subscriptions.Application's SubscriptionSummary record
 * (backend/src/Modules/Subscriptions/.../SubscriptionContracts.cs) — System.Text.Json
 * Web defaults camelCase it. `seatCount` is null for non-per-seat prices.
 * `canceledAt` is the only cancellation signal the backend supports this
 * milestone — there is no scheduled "cancel at period end" (that needs the
 * renewal-cron infrastructure M4/M5 hasn't built yet), cancellation is
 * always immediate. `version` is the optimistic-concurrency row version —
 * carried through for completeness, not currently displayed.
 */
export interface Subscription {
  id: string
  customerId: string
  priceId: string
  status: SubscriptionStatus
  seatCount: number | null
  currentPeriodStart: string
  currentPeriodEnd: string
  trialEnd: string | null
  canceledAt: string | null
  createdAt: string
  version: number
}

/** Exactly one of `newPriceId`/`newSeatCount` is normally provided — the other stays at the subscription's current value. */
export interface ChangePreviewRequest {
  newPriceId?: string
  newSeatCount?: number
}

export interface ProrationPlan {
  priceId: string
  currency: string
  recurringAmountCents: number
}

/**
 * Mirrors BillingPlatform.Subscriptions.Application.ProrationResult exactly —
 * NO client-side money math, every cents field here is pre-computed by the
 * backend's ProrationCalculator. Only `formatCents` at the UI boundary
 * touches these numbers. `remainingCyclePercentage` is a decimal 0-100
 * (e.g. 70.0), not a 0-1 ratio.
 */
export interface ProrationResult {
  currentPlan: ProrationPlan
  newPlan: ProrationPlan
  remainingCycleDays: number
  remainingCyclePercentage: number
  /** Negative — unused old plan/seat allocation. */
  proratedCreditCents: number
  /** Positive — new plan/seat allocation. */
  proratedChargeCents: number
  amountDueImmediatelyCents: number
  nextRegularRenewalAmountCents: number
}

/** Mirrors SubscriptionProrationReceipt — what preview-proration and apply-change both return. */
export interface SubscriptionProrationReceipt {
  subscription: Subscription
  proration: ProrationResult
}
