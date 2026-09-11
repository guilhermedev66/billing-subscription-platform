/**
 * Every event type this codebase actually emits today (grepped from every
 * `IWebhookEventWriter.EnqueueAsync(...)` call site — PaymentTransactionCoordinator.cs,
 * RenewalCronService.cs, SubscriptionMutationCoordinator.cs,
 * SubscriptionChangeBillingOrchestrator.cs). Kept as the literal real set
 * rather than a speculative Stripe-style catalog, so "subscribe to event
 * types" stays honest about what will ever actually fire.
 */
export const KNOWN_WEBHOOK_EVENT_TYPES = [
  'subscription.created',
  'subscription.changed',
  'subscription.renewed',
  'subscription.paused',
  'subscription.resumed',
  'subscription.canceled',
  'invoice.created',
  'invoice.paid',
  'payment.attempted',
  'dunning.outcome',
] as const
