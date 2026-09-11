import { http, HttpResponse } from 'msw'
import type {
  CreateWebhookEndpointInput,
  WebhookDeliveryAttempt,
  WebhookDispatchResult,
  WebhookEndpoint,
  WebhookEvent,
} from '@/features/webhooks/types'

/**
 * Stands in for the real BillingPlatform.Webhooks.Api until it's reachable
 * in this dev/test environment — see features/webhooks/api.ts for the exact
 * endpoint-by-endpoint mapping this mirrors. Seed data covers a healthy
 * endpoint, a degrading one, and an inactive one, plus events across every
 * derived display status (pending, delivered outright, delivered-after-retry,
 * still-retrying) with a realistic attempt chain (500 -> 500 -> 200) for the
 * expandable timeline to have something worth inspecting.
 */
const DAY_MS = 24 * 60 * 60 * 1000
const MINUTE_MS = 60 * 1000

function isoAtOffset(ms: number): string {
  return new Date(Date.now() + ms).toISOString()
}

function problem(status: number, title: string, detail?: string, errors?: Record<string, string[]>) {
  return HttpResponse.json({ status, title, detail, errors }, { status })
}

/** UTF-8 safe base64 encode — mirrors how System.Text.Json serializes a C# byte[]. */
function encodePayload(payload: unknown): string {
  const json = JSON.stringify(payload, null, 0)
  const bytes = new TextEncoder().encode(json)
  let binary = ''
  for (const byte of bytes) binary += String.fromCharCode(byte)
  return btoa(binary)
}

let endpoints: WebhookEndpoint[] = [
  {
    id: 'ep_1',
    organizationId: 'org_demo',
    url: 'https://api.acme.example/webhooks',
    eventTypes: ['invoice.paid', 'invoice.created', 'subscription.renewed', 'subscription.created'],
    active: true,
    createdAt: isoAtOffset(-90 * DAY_MS),
    secret: null,
  },
  {
    id: 'ep_2',
    organizationId: 'org_demo',
    url: 'https://hooks.initech.example/billing',
    eventTypes: ['payment.attempted', 'dunning.outcome', 'subscription.changed'],
    active: true,
    createdAt: isoAtOffset(-45 * DAY_MS),
    secret: null,
  },
  {
    id: 'ep_3',
    organizationId: 'org_demo',
    url: 'https://staging.example.com/webhooks-test',
    eventTypes: ['subscription.created'],
    active: false,
    createdAt: isoAtOffset(-10 * DAY_MS),
    secret: null,
  },
]

interface DeliverySeed {
  statusCode: number | null
  durationMilliseconds: number
  attemptedAtOffsetMs: number
  responseBody: string | null
  error: string | null
}

interface EventSeed {
  id: string
  eventType: string
  aggregateType: string
  aggregateId: string
  occurredAtOffsetMs: number
  dispatchedAtOffsetMs: number | null
  deliveries: DeliverySeed[]
  payload: unknown
}

const eventSeeds: EventSeed[] = [
  {
    id: 'evt_1',
    eventType: 'invoice.paid',
    aggregateType: 'invoice',
    aggregateId: 'inv_1',
    occurredAtOffsetMs: -20 * MINUTE_MS,
    dispatchedAtOffsetMs: -19 * MINUTE_MS,
    deliveries: [
      { statusCode: 200, durationMilliseconds: 84, attemptedAtOffsetMs: -19 * MINUTE_MS, responseBody: '{"received":true}', error: null },
    ],
    payload: { invoice: { id: 'inv_1', status: 'Paid', totalCents: 2900 } },
  },
  {
    id: 'evt_2',
    eventType: 'subscription.changed',
    aggregateType: 'subscription',
    aggregateId: 'sub_2',
    occurredAtOffsetMs: -8 * MINUTE_MS,
    dispatchedAtOffsetMs: null,
    deliveries: [
      {
        statusCode: 500,
        durationMilliseconds: 210,
        attemptedAtOffsetMs: -7 * MINUTE_MS,
        responseBody: '{"error":"internal_error"}',
        error: 'Webhook endpoint returned HTTP 500.',
      },
    ],
    payload: { subscription: { id: 'sub_2', status: 'Active', seatCount: 12 } },
  },
  {
    id: 'evt_3',
    eventType: 'dunning.outcome',
    aggregateType: 'invoice',
    aggregateId: 'inv_3',
    occurredAtOffsetMs: -3 * DAY_MS,
    dispatchedAtOffsetMs: null,
    deliveries: [
      { statusCode: 500, durationMilliseconds: 340, attemptedAtOffsetMs: -3 * DAY_MS, responseBody: '{"error":"internal_error"}', error: 'Webhook endpoint returned HTTP 500.' },
      { statusCode: 502, durationMilliseconds: 190, attemptedAtOffsetMs: -3 * DAY_MS + 5 * MINUTE_MS, responseBody: '{"error":"bad_gateway"}', error: 'Webhook endpoint returned HTTP 502.' },
      { statusCode: null, durationMilliseconds: 30000, attemptedAtOffsetMs: -3 * DAY_MS + 20 * MINUTE_MS, responseBody: null, error: 'The request timed out.' },
      { statusCode: 500, durationMilliseconds: 260, attemptedAtOffsetMs: -3 * DAY_MS + 60 * MINUTE_MS, responseBody: '{"error":"internal_error"}', error: 'Webhook endpoint returned HTTP 500.' },
    ],
    payload: { attempt: { id: 'att_3', outcome: 'Declined' }, invoice: { id: 'inv_3', status: 'Open' } },
  },
  {
    id: 'evt_4',
    eventType: 'subscription.created',
    aggregateType: 'subscription',
    aggregateId: 'sub_4',
    occurredAtOffsetMs: -30 * 1000,
    dispatchedAtOffsetMs: null,
    deliveries: [],
    payload: { subscription: { id: 'sub_4', status: 'Trialing' } },
  },
  {
    id: 'evt_5',
    eventType: 'payment.attempted',
    aggregateType: 'invoice',
    aggregateId: 'inv_5',
    occurredAtOffsetMs: -45 * MINUTE_MS,
    dispatchedAtOffsetMs: -40 * MINUTE_MS,
    deliveries: [
      { statusCode: 500, durationMilliseconds: 412, attemptedAtOffsetMs: -45 * MINUTE_MS, responseBody: '{"error":"internal_error"}', error: 'Webhook endpoint returned HTTP 500.' },
      { statusCode: 200, durationMilliseconds: 96, attemptedAtOffsetMs: -40 * MINUTE_MS, responseBody: '{"received":true}', error: null },
    ],
    payload: { attempt: { id: 'att_5', outcome: 'Succeeded' }, invoice: { id: 'inv_5', status: 'Paid' } },
  },
]

function toEvent(seed: EventSeed): WebhookEvent {
  return {
    id: seed.id,
    organizationId: 'org_demo',
    eventType: seed.eventType,
    aggregateType: seed.aggregateType,
    aggregateId: seed.aggregateId,
    occurredAt: isoAtOffset(seed.occurredAtOffsetMs),
    dispatchedAt: seed.dispatchedAtOffsetMs !== null ? isoAtOffset(seed.dispatchedAtOffsetMs) : null,
    deliveryAttemptCount: seed.deliveries.length,
    rawBody: encodePayload({
      id: seed.id,
      type: seed.eventType,
      aggregateType: seed.aggregateType,
      aggregateId: seed.aggregateId,
      occurredAt: isoAtOffset(seed.occurredAtOffsetMs),
      data: seed.payload,
    }),
  }
}

function toDeliveries(seed: EventSeed): WebhookDeliveryAttempt[] {
  const endpointId = endpoints.find((endpoint) => endpoint.eventTypes.includes(seed.eventType))?.id ?? endpoints[0].id
  return seed.deliveries.map((delivery, index) => ({
    id: `${seed.id}_att_${index + 1}`,
    outboxEventId: seed.id,
    endpointId,
    attemptedAt: isoAtOffset(delivery.attemptedAtOffsetMs),
    statusCode: delivery.statusCode,
    durationMilliseconds: delivery.durationMilliseconds,
    attemptNumber: index + 1,
    retryCount: index,
    responseBody: delivery.responseBody,
    error: delivery.error,
  }))
}

const eventsById = new Map<string, EventSeed>(eventSeeds.map((seed) => [seed.id, seed]))

export const webhooksHandlers = [
  http.post('/api/webhooks/endpoints', async ({ request }) => {
    const body = (await request.json()) as Partial<CreateWebhookEndpointInput>
    if (!body.url || !body.secret || !body.eventTypes || body.eventTypes.length === 0) {
      return problem(400, 'Validation failed.', undefined, { webhook: ['A URL, secret, and at least one event type are required.'] })
    }

    const endpoint: WebhookEndpoint = {
      id: `ep_${endpoints.length + 1}`,
      organizationId: 'org_demo',
      url: body.url,
      eventTypes: body.eventTypes,
      active: true,
      createdAt: new Date().toISOString(),
      secret: body.secret,
    }
    endpoints = [...endpoints, endpoint]
    return HttpResponse.json(endpoint, { status: 201 })
  }),

  http.get('/api/webhooks/endpoints', () => HttpResponse.json(endpoints.map((endpoint) => ({ ...endpoint, secret: null })))),

  http.get('/api/webhooks/events', () => HttpResponse.json(eventSeeds.map(toEvent))),

  http.get('/api/webhooks/events/:id/deliveries', ({ params }) => {
    const seed = eventsById.get(params.id as string)
    if (!seed) return problem(404, 'Webhook event not found.')
    return HttpResponse.json(toDeliveries(seed))
  }),

  http.post('/api/webhooks/dispatch', () => {
    const pending = eventSeeds.filter((seed) => seed.dispatchedAtOffsetMs === null)
    const attempts: WebhookDeliveryAttempt[] = []
    let delivered = 0
    let failed = 0

    for (const seed of pending) {
      const attempt: WebhookDeliveryAttempt = {
        id: `${seed.id}_att_${seed.deliveries.length + 1}`,
        outboxEventId: seed.id,
        endpointId: endpoints.find((endpoint) => endpoint.eventTypes.includes(seed.eventType))?.id ?? endpoints[0].id,
        attemptedAt: new Date().toISOString(),
        statusCode: 200,
        durationMilliseconds: 90,
        attemptNumber: seed.deliveries.length + 1,
        retryCount: seed.deliveries.length,
        responseBody: '{"received":true}',
        error: null,
      }
      seed.deliveries = [...seed.deliveries, { statusCode: 200, durationMilliseconds: 90, attemptedAtOffsetMs: 0, responseBody: '{"received":true}', error: null }]
      seed.dispatchedAtOffsetMs = 0
      attempts.push(attempt)
      delivered += 1
    }

    const result: WebhookDispatchResult = { considered: pending.length, delivered, failed, skipped: 0, attempts }
    return HttpResponse.json(result)
  }),
]
