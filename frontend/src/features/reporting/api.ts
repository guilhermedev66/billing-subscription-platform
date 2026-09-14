import { api } from '@/lib/api/client'
import type { RevenueTotals, WaterfallTotals } from './types'

/**
 * Reporting module endpoints (ReportingEndpoints.cs, M6). Both are org-scoped
 * server-side from the JWT's org claim — never pass an org id from here.
 * `/events/{id}` (single lookup, no list endpoint) isn't wired up yet: there's
 * no dashboard section that needs a specific source event by id this
 * milestone.
 */
export function getRevenueSummary() {
  return api.get<RevenueTotals[]>('/reporting/summary')
}

export function getWaterfall(from: string, to: string): Promise<WaterfallTotals[]> {
  const params = new URLSearchParams({ from, to })
  return api.get<WaterfallTotals[]>(`/reporting/waterfall?${params.toString()}`)
}
