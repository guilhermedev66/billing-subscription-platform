import { Zap } from 'lucide-react'

const timeTravelActions = ['+1 Day', '+7 Days', '+30 Days', 'Advance to Next Anchor']

/**
 * Placeholder for M1 — occupies the persistent top strip so later screens don't
 * have to shift layout when the real virtual clock + cron triggers land in M5
 * (research brief §4). Buttons are inert on purpose, not wired to anything yet.
 */
export function SimulationBar() {
  return (
    <div className="flex h-9 items-center gap-3 overflow-x-auto bg-simbar px-4 text-xs text-simbar-foreground">
      <span className="flex items-center gap-1.5 whitespace-nowrap font-semibold">
        <Zap className="size-3.5" aria-hidden="true" />
        SIMULATION MODE
      </span>
      <span className="whitespace-nowrap text-simbar-foreground/70">Virtual Clock: —</span>
      <div className="flex items-center gap-1.5">
        {timeTravelActions.map((label) => (
          <button
            key={label}
            type="button"
            disabled
            title="Wired up in M5"
            className="whitespace-nowrap rounded border border-white/15 px-2 py-0.5 text-simbar-foreground/70 disabled:cursor-not-allowed"
          >
            {label}
          </button>
        ))}
      </div>
    </div>
  )
}
