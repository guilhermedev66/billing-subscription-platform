import { useSyncExternalStore } from 'react'
import type { PaymentAttempt } from './types'

const MAX_ATTEMPTS = 25

let attempts: PaymentAttempt[] = []
const listeners = new Set<() => void>()

function emit(): void {
  for (const listener of listeners) listener()
}

/**
 * Session-local record of payment attempts, newest first. The backend has no
 * per-invoice attempt-history endpoint yet (M4 backend scope note — see
 * docs/ROADMAP.md), so this is what the dunning cockpit and invoice detail
 * page use to show "what just happened here": it only remembers attempts made
 * from this browser tab this session, not a durable historical ledger. The
 * invoice's own dunningAttemptCount/nextRetryAt (persisted server-side) are
 * still the source of truth for dunning state across sessions/reloads.
 */
export function recordAttempt(attempt: PaymentAttempt): void {
  attempts = [attempt, ...attempts].slice(0, MAX_ATTEMPTS)
  emit()
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

function getSnapshot(): PaymentAttempt[] {
  return attempts
}

export function useRecentAttempts(invoiceId?: string): PaymentAttempt[] {
  const all = useSyncExternalStore(subscribe, getSnapshot)
  return invoiceId ? all.filter((attempt) => attempt.invoiceId === invoiceId) : all
}
