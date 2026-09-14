namespace BillingPlatform.Subscriptions.Application;

// Source facts are complete at the time of the mutation. Reporting must never look up a
// mutable Catalog price while replaying historical subscription events.
public sealed record RevenuePriceTerms(
    Guid PriceId,
    int PriceVersion,
    string Currency,
    string PricingModel,
    string BillingInterval,
    long? FlatUnitAmountCents,
    long? PerSeatUnitAmountCents,
    IReadOnlyList<SubscriptionPriceTier> Tiers);

public sealed record SubscriptionRevenueState(
    string Status,
    Guid PriceId,
    int? SeatCount,
    int SubscriptionVersion,
    RevenuePriceTerms Price);

public sealed record SubscriptionRevenueEvent(
    Guid SourceEventId,
    Guid OrganizationId,
    Guid SubscriptionId,
    string Reason,
    DateTimeOffset OccurredAt,
    SubscriptionRevenueState? Before,
    SubscriptionRevenueState After);

public interface ISubscriptionRevenueEventWriter
{
    Task AppendAsync(
        SubscriptionRevenueEvent sourceEvent,
        CancellationToken cancellationToken = default);
}
