import { http, HttpResponse } from 'msw'
import type { RevenueTotals, WaterfallTotals } from '@/features/reporting/types'

/**
 * Stands in for the real BillingPlatform.Reporting.Api (ReportingEndpoints.cs,
 * M6). Numbers here are hand-derived from the seeded subscriptions/invoices
 * in subscriptionsHandlers.ts/invoicesHandlers.ts so the two stay internally
 * consistent: revenue-bearing subs are sub_1 (Active, $29/mo), sub_2 (Active,
 * 5 seats * $12), sub_4 (PastDue, $29/mo — the same subscription behind the
 * at-risk INV-2026-0003), and sub_8 (Active, $29/mo). Trialing/Unpaid/Canceled/
 * Paused subscriptions contribute $0, matching the real ReportingService's
 * AnnualizedFixedCents convention. There's no `/events` list endpoint on the
 * real API (only a single-id lookup), so it isn't mocked here — the Live
 * Activity Stream section from the UX research note is intentionally not
 * built for that reason (see DashboardPage.tsx's top comment).
 */
const DEFAULT_SUMMARY: RevenueTotals[] = [
  {
    currency: 'USD',
    arrCents: 176_400,
    mrrCents: 14_700,
    atRiskArrCents: 34_800,
    atRiskMrrCents: 2_900,
    revenueBearingSubscriptions: 4,
    meteredExcludedSubscriptions: 0,
  },
]

const DEFAULT_WATERFALL: WaterfallTotals[] = [
  {
    currency: 'USD',
    startingArrCents: 152_400,
    newArrCents: 34_800,
    expansionArrCents: 7_200,
    reactivationArrCents: 0,
    contractionArrCents: 3_600,
    churnArrCents: 14_400,
    endingArrCents: 176_400,
    startingMrrCents: 12_700,
    newMrrCents: 2_900,
    expansionMrrCents: 600,
    reactivationMrrCents: 0,
    contractionMrrCents: 300,
    churnMrrCents: 1_200,
    endingMrrCents: 14_700,
  },
]

/** An org with zero subscriptions at all — no currency has ever appeared, so both arrays are empty. */
export const emptyReportingHandlers = [
  http.get('/api/reporting/summary', () => HttpResponse.json([] satisfies RevenueTotals[])),
  http.get('/api/reporting/waterfall', () => HttpResponse.json([] satisfies WaterfallTotals[])),
]

/** An org with revenue but nothing past-due — the At-Risk Revenue Alert banner must stay hidden. */
export const noAtRiskReportingHandlers = [
  http.get('/api/reporting/summary', () =>
    HttpResponse.json([
      { ...DEFAULT_SUMMARY[0], atRiskArrCents: 0, atRiskMrrCents: 0 },
    ] satisfies RevenueTotals[]),
  ),
  http.get('/api/reporting/waterfall', () => HttpResponse.json(DEFAULT_WATERFALL)),
]

export const reportingHandlers = [
  http.get('/api/reporting/summary', () => HttpResponse.json(DEFAULT_SUMMARY)),
  http.get('/api/reporting/waterfall', () => HttpResponse.json(DEFAULT_WATERFALL)),
]
