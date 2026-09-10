import { api } from '@/lib/api/client'
import type { ChangePreviewRequest, Subscription, SubscriptionProrationReceipt, SubscriptionStatus } from './types'

/**
 * Subscriptions module endpoints, aligned to the real BillingPlatform.Subscriptions.Api
 * contract (SubscriptionsEndpoints.cs / SubscriptionContracts.cs). As with Catalog,
 * SubscriptionStatus is a C# enum with no JsonStringEnumConverter registered
 * server-side, so it serializes as its numeric ordinal — never as a string.
 * This file is the ONLY place that numeric-status wire shape exists; the rest
 * of the app works with the friendly string shape in ./types.
 */

type WireSubscriptionStatus = 0 | 1 | 2 | 3 | 4 | 5

const STATUS_FROM_WIRE: Record<WireSubscriptionStatus, SubscriptionStatus> = {
  0: 'trialing',
  1: 'active',
  2: 'past_due',
  3: 'unpaid',
  4: 'canceled',
  5: 'paused',
}

interface WireSubscription {
  id: string
  organizationId: string
  customerId: string
  priceId: string
  status: WireSubscriptionStatus
  currentPeriodStart: string
  currentPeriodEnd: string
  trialEnd: string | null
  seatCount: number | null
  canceledAt: string | null
  createdAt: string
  version: number
}

interface WireProrationReceipt {
  subscription: WireSubscription
  proration: SubscriptionProrationReceipt['proration']
}

function subscriptionFromWire(wire: WireSubscription): Subscription {
  return {
    id: wire.id,
    customerId: wire.customerId,
    priceId: wire.priceId,
    status: STATUS_FROM_WIRE[wire.status],
    seatCount: wire.seatCount,
    currentPeriodStart: wire.currentPeriodStart,
    currentPeriodEnd: wire.currentPeriodEnd,
    trialEnd: wire.trialEnd,
    canceledAt: wire.canceledAt,
    createdAt: wire.createdAt,
    version: wire.version,
  }
}

function receiptFromWire(wire: WireProrationReceipt): SubscriptionProrationReceipt {
  return { subscription: subscriptionFromWire(wire.subscription), proration: wire.proration }
}

export async function listSubscriptions(): Promise<Subscription[]> {
  const wireSubscriptions = await api.get<WireSubscription[]>('/subscriptions')
  return wireSubscriptions.map(subscriptionFromWire)
}

export async function getSubscription(id: string): Promise<Subscription> {
  const wireSubscription = await api.get<WireSubscription>(`/subscriptions/${id}`)
  return subscriptionFromWire(wireSubscription)
}

export async function previewChange(id: string, request: ChangePreviewRequest): Promise<SubscriptionProrationReceipt> {
  const wireReceipt = await api.post<WireProrationReceipt>(`/subscriptions/${id}/preview-proration`, request)
  return receiptFromWire(wireReceipt)
}

/**
 * Mutates the subscription's price/seat count and returns the receipt (updated
 * subscription + the proration that was charged). Does NOT create an invoice —
 * the Billing module doesn't exist until M4, so the prorated amounts here are
 * not persisted as a line item anywhere yet.
 */
export async function applyChange(id: string, request: ChangePreviewRequest): Promise<SubscriptionProrationReceipt> {
  const wireReceipt = await api.post<WireProrationReceipt>(`/subscriptions/${id}/apply-change`, request)
  return receiptFromWire(wireReceipt)
}

export async function pauseSubscription(id: string): Promise<Subscription> {
  const wireSubscription = await api.post<WireSubscription>(`/subscriptions/${id}/pause`)
  return subscriptionFromWire(wireSubscription)
}

export async function resumeSubscription(id: string): Promise<Subscription> {
  const wireSubscription = await api.post<WireSubscription>(`/subscriptions/${id}/resume`)
  return subscriptionFromWire(wireSubscription)
}

/** Immediate cancellation only this milestone — the backend takes no request body. */
export async function cancelSubscription(id: string): Promise<Subscription> {
  const wireSubscription = await api.post<WireSubscription>(`/subscriptions/${id}/cancel`)
  return subscriptionFromWire(wireSubscription)
}
