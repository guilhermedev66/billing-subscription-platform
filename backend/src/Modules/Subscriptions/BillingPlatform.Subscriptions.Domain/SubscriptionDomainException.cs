namespace BillingPlatform.Subscriptions.Domain;

public sealed class SubscriptionDomainException(string message) : Exception(message);
