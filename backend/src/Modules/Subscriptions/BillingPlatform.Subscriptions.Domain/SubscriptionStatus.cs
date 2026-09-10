namespace BillingPlatform.Subscriptions.Domain;

public enum SubscriptionStatus
{
    Trialing,
    Active,
    PastDue,
    Unpaid,
    Canceled,
    Paused
}
