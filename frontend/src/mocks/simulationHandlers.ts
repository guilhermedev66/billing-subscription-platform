import { http, HttpResponse } from 'msw'

/**
 * Stands in for the real BillingPlatform.Api Simulation endpoints
 * (SimulationEndpoints.cs, M5) — paths and response shapes mirror that
 * contract exactly (see features/simulation/types.ts). Keeps the same
 * offset-based virtual-clock approach as before the boundary rewrite:
 * module-level `offsetMs` added to the real wall-clock `realTime` this
 * session started from.
 */
const DAY_MS = 24 * 60 * 60 * 1000
const realTime = Date.now()
let offsetMs = 0

function problem(status: number, title: string, detail?: string, errors?: Record<string, string[]>) {
  return HttpResponse.json({ status, title, detail, errors }, { status })
}

function currentTimeMs(): number {
  return realTime + offsetMs
}

function nextAnchorMs(): number {
  // A fixed demo anchor 5 days ahead of the current virtual time — the real backend computes this
  // from the soonest subscription renewal / invoice nextRetryAt across the org.
  return currentTimeMs() + 5 * DAY_MS
}

export const simulationHandlers = [
  http.get('/api/simulation/clock', () => HttpResponse.json({ now: new Date(currentTimeMs()).toISOString() })),

  http.post('/api/simulation/advance', async ({ request }) => {
    const body = (await request.json()) as { days?: number }
    if (body.days !== 1 && body.days !== 7 && body.days !== 30) {
      return problem(400, 'Validation failed.', undefined, { days: ['Only 1, 7, or 30 day advances are supported.'] })
    }

    offsetMs += body.days * DAY_MS
    return HttpResponse.json({ now: new Date(currentTimeMs()).toISOString(), advancedDays: body.days })
  }),

  http.post('/api/simulation/advance-to-next-anchor', () => {
    const before = currentTimeMs()
    const anchorMs = nextAnchorMs()
    const advanced = anchorMs > before
    if (advanced) offsetMs = anchorMs - realTime
    return HttpResponse.json({
      now: new Date(currentTimeMs()).toISOString(),
      advanced,
      anchor: new Date(advanced ? anchorMs : before).toISOString(),
    })
  }),

  http.post('/api/simulation/renewal-cron', () => HttpResponse.json({ considered: 3, renewed: [{}, {}] })),

  http.post('/api/simulation/seed', () =>
    HttpResponse.json({
      customer: { id: 'cus_demo_1', name: 'Wayne Enterprises' },
      product: { id: 'prod_demo_1', name: 'Simulation Pro' },
      price: { id: 'price_demo_1' },
      subscription: { id: 'sub_demo_1' },
    }),
  ),
]
