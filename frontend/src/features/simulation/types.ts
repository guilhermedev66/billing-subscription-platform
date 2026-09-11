/**
 * Simulation Clock domain — aligned to the real BillingPlatform.Api
 * contract (SimulationEndpoints.cs / RenewalCronService.cs /
 * SimulationSeeder.cs, M5). This is the boundary rewrite the old
 * speculative version predicted: the real API is much leaner than the
 * original guess — no `nextAnchor` on the clock read, no `realTime`, no
 * reset endpoint, and advance/renewal-cron/seed responses don't carry the
 * clock or webhook-queue counts the speculative version invented. Nested
 * subscription/payment/invoice/customer/product/price shapes below are
 * deliberately typed loosely (`unknown`/minimal shape) rather than fully
 * modeled — the UI here only needs counts and a couple of display fields,
 * and fully mirroring every other module's wire shape would just couple
 * this file to them for no benefit.
 */
export interface SimulationClockState {
  now: string
}

export interface AdvanceResult {
  now: string
  advancedDays: number
}

export interface AdvanceToNextAnchorResult {
  now: string
  advanced: boolean
  anchor: string
}

export interface RenewalCronResult {
  considered: number
  renewed: unknown[]
}

export interface SimulationSeedResult {
  customer: { id: string; name: string }
  product: { id: string; name: string }
  price: { id: string }
  subscription: { id: string } | null
}
