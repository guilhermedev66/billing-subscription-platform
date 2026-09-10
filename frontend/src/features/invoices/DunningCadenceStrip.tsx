import { cn } from '@/lib/cn'
import { DUNNING_STEPS } from './dunningCadence'

interface DunningCadenceStripProps {
  /** invoice.dunningAttemptCount — how many failed attempts have happened so far. */
  attemptCount: number
  /** invoice.status === 'uncollectible' */
  exhausted: boolean
}

const NODE_LABELS = [...DUNNING_STEPS.map((step) => step.label), 'Uncollectible']

/**
 * Compact 5-node stepped view of the dunning cadence (Day 0 Failed -> Day 3
 * Retry 1 -> Day 7 Retry 2 -> Day 14 Final -> Uncollectible), alongside the
 * document status pill and the plain-language dunning callout — not a
 * replacement for either, just a quick "how far along is this" glance.
 */
export function DunningCadenceStrip({ attemptCount, exhausted }: DunningCadenceStripProps) {
  const reachedCount = exhausted ? NODE_LABELS.length : Math.min(attemptCount, DUNNING_STEPS.length)

  return (
    <div className="flex items-start gap-1">
      {NODE_LABELS.map((label, index) => {
        const reached = index < reachedCount
        const isFinal = label === 'Uncollectible'

        return (
          <div key={label} className="flex flex-1 flex-col items-center gap-1">
            <div
              className={cn(
                'h-1.5 w-full rounded-full',
                reached ? (isFinal ? 'bg-rose-500' : 'bg-amber-500') : 'bg-surface-muted',
              )}
              aria-hidden="true"
            />
            <span className={cn('text-center text-[10px] leading-tight', reached ? 'text-foreground' : 'text-muted-foreground')}>
              {label}
            </span>
          </div>
        )
      })}
    </div>
  )
}
