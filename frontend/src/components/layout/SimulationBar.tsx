import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Zap } from 'lucide-react'
import { useState } from 'react'
import {
  advanceClockByDays,
  advanceToNextAnchor,
  getClockState,
  seedDemoDataset,
  triggerRenewalCron,
} from '@/features/simulation/api'
import { sweepDunning } from '@/features/payments/api'
import { ApiError } from '@/lib/api/client'

const clockFormatter = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeStyle: 'short' })
const dateFormatter = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium' })

function messageFor(error: unknown, fallback: string): string {
  return error instanceof ApiError ? error.message : fallback
}

/**
 * Fully wired M5 Simulation Bar — real virtual-clock time travel, renewal
 * cron, dunning sweep, and demo dataset seeding (docs/ROADMAP.md M5: "full
 * virtual-clock time-travel API ... trigger renewal cron, process dunning
 * sweep, demo dataset seeder"). Any successful action can move subscriptions,
 * invoices, payments, or webhooks anywhere in the app, so every mutation
 * invalidates the whole query cache rather than a narrow key.
 */
export function SimulationBar() {
  const queryClient = useQueryClient()
  const [status, setStatus] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const {
    data: clock,
    isPending: clockPending,
    isError: clockError,
    refetch: refetchClock,
  } = useQuery({ queryKey: ['simulation', 'clock'], queryFn: getClockState })

  function afterMutation() {
    queryClient.invalidateQueries()
  }

  const advanceMutation = useMutation({
    mutationFn: (days: 1 | 7 | 30) => advanceClockByDays(days),
    onSuccess: (result) => {
      setError(null)
      setStatus(`Advanced ${result.advancedDays} day${result.advancedDays === 1 ? '' : 's'} · new time ${clockFormatter.format(new Date(result.now))}`)
      afterMutation()
    },
    onError: (err) => {
      setStatus(null)
      setError(messageFor(err, "Couldn't advance the virtual clock."))
    },
  })

  const anchorMutation = useMutation({
    mutationFn: advanceToNextAnchor,
    onSuccess: (result) => {
      setError(null)
      setStatus(
        result.advanced
          ? `Advanced to next anchor — ${dateFormatter.format(new Date(result.anchor))}`
          : 'Nothing due yet — clock unchanged.',
      )
      afterMutation()
    },
    onError: (err) => {
      setStatus(null)
      setError(messageFor(err, "Couldn't advance to the next anchor."))
    },
  })

  const renewalCronMutation = useMutation({
    mutationFn: triggerRenewalCron,
    onSuccess: (result) => {
      setError(null)
      setStatus(`Renewal cron: ${result.considered} considered, ${result.renewed.length} renewed`)
      afterMutation()
    },
    onError: (err) => {
      setStatus(null)
      setError(messageFor(err, "Couldn't trigger the renewal cron."))
    },
  })

  const dunningSweepMutation = useMutation({
    mutationFn: sweepDunning,
    onSuccess: (result) => {
      setError(null)
      setStatus(
        `Dunning sweep: ${result.considered} considered, ${result.succeeded} succeeded, ${result.failed} failed, ${result.becameUncollectible} became uncollectible`,
      )
      afterMutation()
    },
    onError: (err) => {
      setStatus(null)
      setError(messageFor(err, "Couldn't process the dunning sweep."))
    },
  })

  const seedMutation = useMutation({
    mutationFn: seedDemoDataset,
    onSuccess: (result) => {
      setError(null)
      setStatus(`Demo dataset ready — customer: ${result.customer.name}`)
      afterMutation()
    },
    onError: (err) => {
      setStatus(null)
      setError(messageFor(err, "Couldn't seed the demo dataset."))
    },
  })

  const busy =
    advanceMutation.isPending ||
    anchorMutation.isPending ||
    renewalCronMutation.isPending ||
    dunningSweepMutation.isPending ||
    seedMutation.isPending

  // Every action below depends on a working clock read — if it's failing, clicking
  // them would just fail too, so keep them disabled until the clock recovers.
  const actionsDisabled = busy || clockError

  const actions: { label: string; ariaLabel: string; onClick: () => void }[] = [
    { label: '+1 Day', ariaLabel: 'Advance virtual clock by 1 day', onClick: () => advanceMutation.mutate(1) },
    { label: '+7 Days', ariaLabel: 'Advance virtual clock by 7 days', onClick: () => advanceMutation.mutate(7) },
    { label: '+30 Days', ariaLabel: 'Advance virtual clock by 30 days', onClick: () => advanceMutation.mutate(30) },
    { label: 'Advance to Next Anchor', ariaLabel: 'Advance virtual clock to the next scheduled anchor', onClick: () => anchorMutation.mutate() },
    { label: 'Trigger Renewal Cron', ariaLabel: 'Trigger the subscription renewal cron', onClick: () => renewalCronMutation.mutate() },
    { label: 'Process Dunning Sweep', ariaLabel: 'Process the dunning sweep for overdue invoices', onClick: () => dunningSweepMutation.mutate() },
    { label: 'Seed Demo Dataset', ariaLabel: 'Seed a demo customer, product, price, and subscription', onClick: () => seedMutation.mutate() },
  ]

  return (
    <div className="flex h-9 items-center gap-3 overflow-x-auto bg-simbar px-4 text-xs text-simbar-foreground">
      <span className="flex items-center gap-1.5 whitespace-nowrap font-semibold">
        <Zap className="size-3.5" aria-hidden="true" />
        SIMULATION MODE
      </span>
      {clockError ? (
        <span role="alert" className="flex items-center gap-1.5 whitespace-nowrap text-rose-200">
          Couldn't load the virtual clock.
          <button
            type="button"
            onClick={() => refetchClock()}
            className="underline underline-offset-2 hover:text-white"
          >
            Retry
          </button>
        </span>
      ) : (
        <span className="whitespace-nowrap text-simbar-foreground/70">
          Virtual Clock: {clock ? clockFormatter.format(new Date(clock.now)) : clockPending ? 'Loading…' : '—'}
        </span>
      )}
      <div className="flex shrink-0 items-center gap-1.5">
        {actions.map((action) => (
          <button
            key={action.label}
            type="button"
            disabled={actionsDisabled}
            aria-label={action.ariaLabel}
            onClick={action.onClick}
            className="whitespace-nowrap rounded border border-white/15 px-2 py-0.5 text-simbar-foreground/70 hover:bg-white/10 hover:text-simbar-foreground disabled:cursor-not-allowed disabled:opacity-60"
          >
            {action.label}
          </button>
        ))}
      </div>
      <div className="min-w-0 flex-1">
        {error && (
          <p role="alert" className="truncate whitespace-nowrap text-rose-200">
            {error}
          </p>
        )}
        {!error && status && (
          <p role="status" aria-live="polite" className="truncate whitespace-nowrap text-simbar-foreground/70">
            {status}
          </p>
        )}
      </div>
    </div>
  )
}
