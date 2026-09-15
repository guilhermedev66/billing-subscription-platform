import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/Card'
import { billingStatusTokens } from '@/components/ui/StatusBadge'
import { cn } from '@/lib/cn'
import { formatCents } from '@/lib/money'
import type { WaterfallTotals } from './types'
import { netMrrDeltaCents } from './waterfallMath'

interface WaterfallRow {
  label: string
  deltaCents: number
  barClassName: string
}

/**
 * Four categories only, matching the approved M6 UX research exactly (New =
 * success, Expansion = info, Contraction = warning, Churn = destructive,
 * reusing the existing billingStatusTokens dots rather than restating raw
 * Tailwind classes) — the backend also reports `reactivationMrrCents`, but
 * that's real revenue movement with no bar here by design, not hidden data:
 * it still flows into Net Movement via `netMrrDeltaCents`, which is not
 * re-derived from these four rows (see that function's comment).
 */
function rowsFor(totals: WaterfallTotals): WaterfallRow[] {
  return [
    { label: 'New MRR', deltaCents: totals.newMrrCents, barClassName: billingStatusTokens.success.dot },
    { label: 'Expansion MRR', deltaCents: totals.expansionMrrCents, barClassName: billingStatusTokens.info.dot },
    { label: 'Contraction MRR', deltaCents: -totals.contractionMrrCents, barClassName: billingStatusTokens.warning.dot },
    { label: 'Churned MRR', deltaCents: -totals.churnMrrCents, barClassName: billingStatusTokens.destructive.dot },
  ]
}

function signedFormat(cents: number, currency: string): string {
  return `${cents > 0 ? '+' : ''}${formatCents(cents, currency)}`
}

interface WaterfallCardProps {
  totals: WaterfallTotals
  /** Shown in the card title when the org has more than one active currency. */
  currencyLabel?: string
}

export function WaterfallCard({ totals, currencyLabel }: WaterfallCardProps) {
  const rows = rowsFor(totals)
  const maxAbsDelta = Math.max(...rows.map((row) => Math.abs(row.deltaCents)), 1)
  const netCents = netMrrDeltaCents(totals)

  return (
    <Card>
      <CardHeader>
        <CardTitle>MRR Waterfall (Last 30 Days){currencyLabel ? ` · ${currencyLabel}` : ''}</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {rows.map((row) => (
          <div key={row.label} className="flex flex-col gap-1.5">
            <div className="flex items-center justify-between text-sm">
              <span className="text-muted-foreground">{row.label}</span>
              <span
                className={cn(
                  'font-mono tabular-nums font-medium',
                  row.deltaCents > 0 && 'text-emerald-600 dark:text-emerald-400',
                  row.deltaCents < 0 && 'text-rose-600 dark:text-rose-400',
                  row.deltaCents === 0 && 'text-muted-foreground',
                )}
              >
                {signedFormat(row.deltaCents, totals.currency)}
              </span>
            </div>
            <div className="h-2 w-full overflow-hidden rounded-full bg-surface-muted" aria-hidden="true">
              <div
                className={cn('h-full rounded-full', row.barClassName)}
                style={{ width: `${(Math.abs(row.deltaCents) / maxAbsDelta) * 100}%` }}
              />
            </div>
          </div>
        ))}

        <div className="flex items-center justify-between border-t border-border pt-3 text-sm">
          <span className="font-semibold text-foreground">Net Movement</span>
          <span
            className={cn(
              'font-mono tabular-nums text-base font-semibold',
              netCents > 0 && 'text-emerald-600 dark:text-emerald-400',
              netCents < 0 && 'text-rose-600 dark:text-rose-400',
              netCents === 0 && 'text-foreground',
            )}
          >
            {signedFormat(netCents, totals.currency)}
          </span>
        </div>
      </CardContent>
    </Card>
  )
}
