/**
 * Reporting module wire shape — matches ReportingContracts.cs exactly
 * (backend/src/Modules/Reporting/BillingPlatform.Reporting.Application). All
 * money fields are integer cents; System.Text.Json Web defaults camelCase
 * these record properties, so no wire-mapping layer is needed here (unlike
 * Subscriptions/Catalog, which serialize enums as numeric ordinals).
 */
export interface RevenueTotals {
  currency: string
  arrCents: number
  mrrCents: number
  atRiskArrCents: number
  atRiskMrrCents: number
  revenueBearingSubscriptions: number
  meteredExcludedSubscriptions: number
}

/**
 * `endingXxxCents` is the backend's authoritative ending run-rate for the
 * window — always prefer it over re-deriving from the movement categories
 * below (ReportingService.WaterfallAsync computes `ending` server-side; it
 * does not currently fold `reactivation` into that figure, so summing
 * new+expansion-contraction-churn client-side can disagree with `ending`
 * whenever reactivation is non-zero).
 */
export interface WaterfallTotals {
  currency: string
  startingArrCents: number
  newArrCents: number
  expansionArrCents: number
  reactivationArrCents: number
  contractionArrCents: number
  churnArrCents: number
  endingArrCents: number
  startingMrrCents: number
  newMrrCents: number
  expansionMrrCents: number
  reactivationMrrCents: number
  contractionMrrCents: number
  churnMrrCents: number
  endingMrrCents: number
}
