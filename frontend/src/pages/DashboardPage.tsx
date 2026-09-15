import { useQuery } from '@tanstack/react-query'
import { AlertTriangle, LayoutDashboard, RefreshCw } from 'lucide-react'
import { useMemo, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { Button } from '@/components/ui/Button'
import { Card, CardContent } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { ErrorState } from '@/components/ui/ErrorState'
import { SignedAmount } from '@/components/ui/SignedAmount'
import { CardSkeleton, CardSkeletonGrid } from '@/components/ui/Skeleton'
import { listInvoices } from '@/features/invoices/api'
import { byUrgency, isAtRisk } from '@/features/invoices/atRisk'
import { relativeTime } from '@/features/invoices/dunningCadence'
import { getRevenueSummary, getWaterfall } from '@/features/reporting/api'
import type { WaterfallTotals } from '@/features/reporting/types'
import { WaterfallCard } from '@/features/reporting/WaterfallCard'
import { netMrrDeltaCents } from '@/features/reporting/waterfallMath'
import { getClockState } from '@/features/simulation/api'
import { listSubscriptions } from '@/features/subscriptions/api'
import { cn } from '@/lib/cn'
import { formatCents } from '@/lib/money'
import { useNow } from '@/lib/useNow'

const DAY_MS = 24 * 60 * 60 * 1000
const WATERFALL_WINDOW_DAYS = 30

interface KpiCardProps {
  label: string
  value: ReactNode
  hint?: string
  valueClassName?: string
}

function KpiCard({ label, value, hint, valueClassName }: KpiCardProps) {
  return (
    <Card>
      <CardContent className="flex flex-col gap-1 p-4">
        <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</p>
        <p className={cn('font-mono tabular-nums text-2xl font-semibold text-foreground sm:text-3xl', valueClassName)}>
          {value}
        </p>
        {hint && <p className="text-xs text-muted-foreground">{hint}</p>}
      </CardContent>
    </Card>
  )
}

function NetGrowthKpi({
  currency,
  totals,
  isPending,
  isError,
  onRetry,
}: {
  currency: string
  totals: WaterfallTotals | undefined
  isPending: boolean
  isError: boolean
  onRetry: () => void
}) {
  if (isError) {
    return (
      <Card>
        <CardContent className="flex flex-col gap-1 p-4">
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Net MRR Growth (30d)</p>
          <button
            type="button"
            onClick={onRetry}
            aria-label={`Retry loading Net MRR Growth for ${currency}`}
            className="text-left text-sm text-rose-600 dark:text-rose-400"
          >
            Couldn't load — retry
          </button>
        </CardContent>
      </Card>
    )
  }

  if (isPending || !totals) {
    return <CardSkeleton />
  }

  const net = netMrrDeltaCents(totals)
  return (
    <KpiCard
      label="Net MRR Growth (30d)"
      value={<SignedAmount cents={net} currency={totals.currency} />}
      valueClassName={cn(net > 0 && 'text-emerald-600 dark:text-emerald-400', net < 0 && 'text-rose-600 dark:text-rose-400')}
      hint={`New ${formatCents(totals.newMrrCents, totals.currency)} · Churn -${formatCents(totals.churnMrrCents, totals.currency)}`}
    />
  )
}

/**
 * M6 Executive Dashboard — hero KPI grid, At-Risk Revenue Alert banner, and
 * MRR Waterfall, built directly on the real Reporting module contract
 * (ReportingContracts.cs). Deliberately does NOT include the "Live Activity
 * Stream" / "Recent Billing & Audit Feed" section from the original UX
 * research note: the only reporting events endpoint is a single-event lookup
 * by id (`GET /api/reporting/events/{sourceEventId}`), there is no list
 * endpoint to page through recent events, so a feed here would have to be
 * either fabricated or reassembled from unrelated per-module lists dressed up
 * as a unified "audit feed" — both are exactly what this project's anti-fake-data
 * rule forbids. Same reasoning for "Export CSV": no export endpoint exists.
 */
export function DashboardPage() {
  const {
    data: clock,
    isPending: clockPending,
    isError: clockError,
  } = useQuery({ queryKey: ['simulation', 'clock'], queryFn: getClockState })

  const {
    data: summary,
    isPending: summaryPending,
    isError: summaryError,
    refetch: refetchSummary,
  } = useQuery({ queryKey: ['reporting', 'summary'], queryFn: getRevenueSummary })

  const {
    data: subscriptions,
    isPending: subscriptionsPending,
    isError: subscriptionsError,
    refetch: refetchSubscriptions,
  } = useQuery({ queryKey: ['subscriptions'], queryFn: listSubscriptions })

  const {
    data: invoices,
    isPending: invoicesPending,
    isError: invoicesError,
    refetch: refetchInvoices,
  } = useQuery({ queryKey: ['invoices'], queryFn: listInvoices })

  const now = useNow(60_000)

  // Virtual-clock-relative window (not real wall time) — mirrors the SimulationBar's
  // own ['simulation', 'clock'] query above, so this stays correct across time-jumps.
  const windowTo = clock?.now
  const windowFrom = windowTo ? new Date(new Date(windowTo).getTime() - WATERFALL_WINDOW_DAYS * DAY_MS).toISOString() : undefined

  const {
    data: waterfall,
    isPending: waterfallPending,
    isError: waterfallError,
    refetch: refetchWaterfall,
  } = useQuery({
    queryKey: ['reporting', 'waterfall', windowFrom, windowTo],
    queryFn: () => getWaterfall(windowFrom as string, windowTo as string),
    enabled: Boolean(windowFrom && windowTo),
  })

  const waterfallByCurrency = useMemo(() => {
    const map = new Map<string, WaterfallTotals>()
    for (const entry of waterfall ?? []) map.set(entry.currency, entry)
    return map
  }, [waterfall])

  const atRiskInvoices = useMemo(() => (invoices ?? []).filter(isAtRisk).sort(byUrgency), [invoices])

  const subscriptionCounts = useMemo(() => {
    const list = subscriptions ?? []
    return {
      active: list.filter((s) => s.status === 'active').length,
      trialing: list.filter((s) => s.status === 'trialing').length,
      pastDue: list.filter((s) => s.status === 'past_due').length,
    }
  }, [subscriptions])

  const isPending = clockPending || summaryPending || subscriptionsPending || invoicesPending
  const isError = clockError || summaryError || subscriptionsError || invoicesError

  function refetchAll() {
    refetchSummary()
    refetchSubscriptions()
    refetchInvoices()
    refetchWaterfall()
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold tracking-tight text-foreground">Overview</h1>
        <Button variant="secondary" size="sm" onClick={refetchAll}>
          <RefreshCw className="size-4" aria-hidden="true" />
          Refresh
        </Button>
      </div>

      {isPending && <CardSkeletonGrid count={4} label="Loading revenue overview" />}

      {isError && <ErrorState message="Couldn't load the revenue overview." onRetry={refetchAll} />}

      {!isPending && !isError && summary && summary.length === 0 && (
        <EmptyState
          icon={LayoutDashboard}
          title="No revenue yet"
          description="MRR and ARR appear here once a subscription is active."
        />
      )}

      {!isPending && !isError && summary && summary.length > 0 && (
        <>
          {summary.map((entry, index) => (
            <div key={entry.currency} className="flex flex-col gap-3">
              {summary.length > 1 && (
                <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{entry.currency}</p>
              )}
              <div
                className={cn(
                  'grid grid-cols-1 gap-4 sm:grid-cols-2',
                  index === 0 ? 'lg:grid-cols-4' : 'lg:grid-cols-3',
                )}
              >
                <KpiCard label="MRR" value={formatCents(entry.mrrCents, entry.currency)} />
                <KpiCard
                  label="ARR"
                  value={formatCents(entry.arrCents, entry.currency)}
                  hint="Normalized 12x MRR"
                />
                {index === 0 && (
                  <KpiCard
                    label="Active Subscriptions"
                    value={String(subscriptionCounts.active)}
                    hint={`${subscriptionCounts.trialing} trial · ${subscriptionCounts.pastDue} past due`}
                  />
                )}
                <NetGrowthKpi
                  currency={entry.currency}
                  totals={waterfallByCurrency.get(entry.currency)}
                  isPending={waterfallPending}
                  isError={waterfallError}
                  onRetry={() => refetchWaterfall()}
                />
              </div>
            </div>
          ))}

          {summary
            .filter((entry) => entry.atRiskArrCents > 0)
            .map((entry) => {
              const currencyAtRisk = atRiskInvoices.filter((invoice) => invoice.currency === entry.currency)
              const earliest = currencyAtRisk.find((invoice) => invoice.nextRetryAt)

              return (
                <div
                  key={entry.currency}
                  role="alert"
                  className="flex flex-col gap-3 rounded-md border border-amber-200 bg-amber-50 px-4 py-3 text-amber-900 dark:border-amber-800 dark:bg-amber-950/40 dark:text-amber-200 sm:flex-row sm:items-center sm:justify-between"
                >
                  <div className="flex items-start gap-3">
                    <AlertTriangle className="mt-0.5 size-5 shrink-0" aria-hidden="true" />
                    <div className="flex flex-col gap-1 text-sm">
                      <p className="font-medium">
                        {formatCents(entry.atRiskMrrCents, entry.currency)} At-Risk MRR across {currencyAtRisk.length} past-due
                        subscription{currencyAtRisk.length === 1 ? '' : 's'} in active dunning.
                      </p>
                      {earliest?.nextRetryAt && (
                        <p>
                          Next retry: {earliest.invoiceNumber} {relativeTime(earliest.nextRetryAt, now)} (attempt{' '}
                          {earliest.dunningAttemptCount} of 4)
                        </p>
                      )}
                    </div>
                  </div>
                  <Link
                    to="/payments"
                    className="inline-flex h-8 shrink-0 items-center justify-center gap-2 rounded-md border border-amber-300 bg-amber-100/60 px-3 text-sm font-medium text-amber-900 hover:bg-amber-100 dark:border-amber-700 dark:bg-amber-900/40 dark:text-amber-100 dark:hover:bg-amber-900/60"
                  >
                    Open Dunning Cockpit →
                  </Link>
                </div>
              )
            })}

          <div className="flex flex-col gap-6">
            {waterfallPending && !waterfall && <CardSkeletonGrid count={1} label="Loading MRR waterfall" />}
            {waterfallError && !waterfall && (
              <ErrorState message="Couldn't load the MRR waterfall." onRetry={() => refetchWaterfall()} />
            )}
            {waterfall?.map((totals) => (
              <WaterfallCard
                key={totals.currency}
                totals={totals}
                currencyLabel={summary.length > 1 ? totals.currency : undefined}
              />
            ))}
          </div>
        </>
      )}
    </div>
  )
}
