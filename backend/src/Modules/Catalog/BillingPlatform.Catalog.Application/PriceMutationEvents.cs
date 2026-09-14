namespace BillingPlatform.Catalog.Application;

public sealed class PriceConcurrencyException()
    : Exception("The price was modified by another request.");

public sealed record CatalogPriceTerms(
    Guid PriceId,
    int PriceVersion,
    string Currency,
    string PricingModel,
    string BillingInterval,
    long? FlatUnitAmountCents,
    long? PerSeatUnitAmountCents,
    IReadOnlyList<PricingTierInput> Tiers);

public sealed record CatalogPriceMutationEvent(
    Guid SourceEventId,
    Guid OrganizationId,
    Guid PriceId,
    DateTimeOffset OccurredAt,
    CatalogPriceTerms Before,
    CatalogPriceTerms After);

public interface IPriceMutationEventWriter
{
    Task AppendAsync(
        CatalogPriceMutationEvent sourceEvent,
        CancellationToken cancellationToken = default);
}

public interface IPriceMutationCoordinator
{
    Task<PriceSummary?> UpdateAsync(
        Guid organizationId,
        Guid priceId,
        UpdatePriceCommand command,
        CancellationToken cancellationToken = default);
}
