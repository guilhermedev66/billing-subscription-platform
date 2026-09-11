import { api } from '@/lib/api/client'
import type {
  AdvanceResult,
  AdvanceToNextAnchorResult,
  RenewalCronResult,
  SimulationClockState,
  SimulationSeedResult,
} from './types'

/**
 * Simulation Clock endpoints, aligned to the real BillingPlatform.Api
 * contract (SimulationEndpoints.cs, RequireAuthorization("simulation-operator") —
 * the `simulation_operator` JWT claim is issued automatically for any org
 * with SimulationModeEnabled=true, the default for every org). None of these
 * mutating endpoints check for an Idempotency-Key header — unlike the rest
 * of this app's mutations, they're intentionally safe/expected to be
 * invoked repeatedly (an operator console re-advancing time on each click
 * is the point, not a duplicate to guard against). `sweepDunning` in
 * features/payments/api.ts is the one dunning-sweep action the Simulation
 * Bar calls — it lives there because it wraps the real Payments endpoint.
 */
export function getClockState() {
  return api.get<SimulationClockState>('/simulation/clock')
}

export function advanceClockByDays(days: 1 | 7 | 30) {
  return api.post<AdvanceResult>('/simulation/advance', { days })
}

export function advanceToNextAnchor() {
  return api.post<AdvanceToNextAnchorResult>('/simulation/advance-to-next-anchor')
}

export function triggerRenewalCron() {
  return api.post<RenewalCronResult>('/simulation/renewal-cron')
}

export function seedDemoDataset() {
  return api.post<SimulationSeedResult>('/simulation/seed')
}
