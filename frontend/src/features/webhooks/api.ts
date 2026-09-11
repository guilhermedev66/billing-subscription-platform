import { api } from '@/lib/api/client'
import { idempotencyHeaders } from '@/lib/idempotency'
import type {
  CreateWebhookEndpointInput,
  WebhookDeliveryAttempt,
  WebhookDispatchResult,
  WebhookEndpoint,
  WebhookEvent,
  WebhookEventDisplayStatus,
} from './types'

/**
 * Webhooks module endpoints, aligned to the real BillingPlatform.Webhooks.Api
 * contract (WebhookEndpoints.cs). No enum-ordinal wire translation needed
 * here — everything is already strings/primitives on the wire, unlike
 * invoices/payments/subscriptions.
 */

/** New endpoints are a real created row (not naturally idempotent-safe to retry), so this carries a key like every other create in this app — even though the handler doesn't check it yet. */
export function createWebhookEndpoint(input: CreateWebhookEndpointInput) {
  return api.post<WebhookEndpoint>('/webhooks/endpoints', input, { headers: idempotencyHeaders() })
}

export function listWebhookEndpoints() {
  return api.get<WebhookEndpoint[]>('/webhooks/endpoints')
}

export function listWebhookEvents() {
  return api.get<WebhookEvent[]>('/webhooks/events')
}

/** 404 (via ApiError) when the event isn't in the caller's org or doesn't exist. */
export function listWebhookDeliveries(eventId: string) {
  return api.get<WebhookDeliveryAttempt[]>(`/webhooks/events/${eventId}/deliveries`)
}

/**
 * Org-wide "process everything due now" — there is no per-event resend
 * endpoint. An operator trigger, same style as the Simulation Bar's
 * renewal-cron action, so no Idempotency-Key (running it twice just means
 * the second run has less to do).
 */
export function dispatchPendingDeliveries() {
  return api.post<WebhookDispatchResult>('/webhooks/dispatch', undefined)
}

/**
 * The backend has no status field — derive one for display. Retries run
 * with exponential backoff indefinitely (no max-attempts cutoff server-side),
 * so there's no terminal "exhausted" state to represent.
 */
export function eventDisplayStatus(event: Pick<WebhookEvent, 'dispatchedAt' | 'deliveryAttemptCount'>): WebhookEventDisplayStatus {
  if (event.dispatchedAt) return 'delivered'
  return event.deliveryAttemptCount > 0 ? 'retrying' : 'pending'
}

export interface DecodedEventPayload {
  /** Pretty-printed JSON when rawBody parsed cleanly, otherwise the raw decoded text. */
  text: string
  isJson: boolean
}

/**
 * rawBody is a C# byte[] (UTF-8 JSON bytes), which serializes as base64 on
 * the wire. Decode, then pretty-print — falling back to the raw decoded
 * string if it isn't valid JSON so a row never crashes on bad/legacy data.
 */
export function decodeEventPayload(rawBodyBase64: string): DecodedEventPayload {
  try {
    const binary = atob(rawBodyBase64)
    const bytes = Uint8Array.from(binary, (char) => char.charCodeAt(0))
    const text = new TextDecoder('utf-8').decode(bytes)
    try {
      return { text: JSON.stringify(JSON.parse(text), null, 2), isJson: true }
    } catch {
      return { text, isJson: false }
    }
  } catch {
    return { text: rawBodyBase64, isJson: false }
  }
}
