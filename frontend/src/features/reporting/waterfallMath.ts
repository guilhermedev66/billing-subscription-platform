import type { WaterfallTotals } from './types'

/**
 * The one authoritative "how much did MRR move this window" number — used by
 * both the Net Growth hero card and the Waterfall card's Net Movement row, so
 * they never disagree. Deliberately `ending - starting` (the backend's own
 * figure) rather than `new + expansion - contraction - churn`, because
 * ReportingService.WaterfallAsync does not fold `reactivation` into `ending`
 * server-side — summing the four rendered categories client-side could
 * silently diverge from the backend's own ending run-rate whenever
 * reactivation is non-zero.
 */
export function netMrrDeltaCents(totals: WaterfallTotals): number {
  return totals.endingMrrCents - totals.startingMrrCents
}
