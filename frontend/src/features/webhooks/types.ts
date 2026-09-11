/**
 * Webhooks domain — aligned to the real BillingPlatform.Webhooks.Api contract
 * (WebhookEndpoints.cs / WebhookContracts.cs / WebhookService.cs). The real
 * backend is deliberately leaner than earlier speculative drafts of this
 * file: the HMAC secret is client-supplied and echoed back only in the
 * create response (no reveal/regenerate endpoint), there's no per-endpoint
 * activate/deactivate toggle, and delivery is triggered org-wide via
 * POST /webhooks/dispatch rather than per-event resend. See ./api.ts for the
 * endpoint-by-endpoint mapping.
 */

/** Derived client-side — the backend has no status field. See ./api.ts's eventDisplayStatus. */
export type WebhookEventDisplayStatus = 'delivered' | 'pending' | 'retrying'

export interface WebhookEndpoint {
  id: string
  organizationId: string
  url: string
  eventTypes: string[]
  active: boolean
  createdAt: string
  /** Only populated on the create response — every other read has this as null. */
  secret: string | null
}

export interface CreateWebhookEndpointInput {
  url: string
  secret: string
  eventTypes: string[]
}

export interface WebhookEvent {
  id: string
  organizationId: string
  eventType: string
  aggregateType: string
  aggregateId: string
  occurredAt: string
  dispatchedAt: string | null
  deliveryAttemptCount: number
  /** Base64-encoded UTF-8 JSON — decode with decodeEventPayload (./api.ts). */
  rawBody: string
}

/**
 * No request headers/body are captured per attempt server-side — only
 * response-side info plus timing. The event's own decoded rawBody is the
 * closest thing to "what was sent" (same payload goes to every subscribed
 * endpoint for that event).
 */
export interface WebhookDeliveryAttempt {
  id: string
  outboxEventId: string
  endpointId: string
  attemptedAt: string
  statusCode: number | null
  durationMilliseconds: number
  attemptNumber: number
  retryCount: number
  responseBody: string | null
  error: string | null
}

export interface WebhookDispatchResult {
  considered: number
  delivered: number
  failed: number
  skipped: number
  attempts: WebhookDeliveryAttempt[]
}
