using BillingPlatform.Catalog.Domain;

namespace BillingPlatform.Catalog.Application;

public sealed record CreateProductCommand(string Name, string Description, bool Active);

public sealed record UpdateProductCommand(string Name, string Description, bool Active);

public sealed record ProductSummary(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string Description,
    bool Active);

public sealed record CreatePriceCommand(
    Guid ProductId,
    PricingModel PricingModel,
    string Currency,
    BillingInterval BillingInterval,
    long? FlatUnitAmountCents,
    long? PerSeatUnitAmountCents,
    long? MeteredUnitAmountCents,
    MeteredAggregation? MeteredAggregation,
    IReadOnlyList<PricingTierInput> Tiers,
    int? TrialDays);

public sealed record UpdatePriceCommand(
    PricingModel PricingModel,
    string Currency,
    BillingInterval BillingInterval,
    long? FlatUnitAmountCents,
    long? PerSeatUnitAmountCents,
    long? MeteredUnitAmountCents,
    MeteredAggregation? MeteredAggregation,
    IReadOnlyList<PricingTierInput> Tiers,
    int? TrialDays);

public sealed record PricingTierInput(
    int StartingUnit,
    int? EndingUnit,
    long UnitAmountCents);

public sealed record PricingTierSummary(
    Guid Id,
    int StartingUnit,
    int? EndingUnit,
    long UnitAmountCents);

public sealed record PriceSummary(
    Guid Id,
    Guid ProductId,
    Guid OrganizationId,
    PricingModel PricingModel,
    string Currency,
    BillingInterval BillingInterval,
    long? FlatUnitAmountCents,
    long? PerSeatUnitAmountCents,
    long? MeteredUnitAmountCents,
    MeteredAggregation? MeteredAggregation,
    IReadOnlyList<PricingTierSummary> Tiers,
    int? TrialDays,
    int Version = 1);

public interface IProductService
{
    Task<ProductSummary> CreateAsync(
        Guid organizationId,
        CreateProductCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductSummary>> ListAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<ProductSummary?> GetAsync(
        Guid organizationId,
        Guid productId,
        CancellationToken cancellationToken = default);

    Task<ProductSummary?> UpdateAsync(
        Guid organizationId,
        Guid productId,
        UpdateProductCommand command,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid organizationId,
        Guid productId,
        CancellationToken cancellationToken = default);
}

public interface IPriceService
{
    Task<PriceSummary?> CreateAsync(
        Guid organizationId,
        CreatePriceCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PriceSummary>> ListAsync(
        Guid organizationId,
        Guid? productId,
        CancellationToken cancellationToken = default);

    Task<PriceSummary?> GetAsync(
        Guid organizationId,
        Guid priceId,
        CancellationToken cancellationToken = default);

    Task<PriceSummary?> UpdateAsync(
        Guid organizationId,
        Guid priceId,
        UpdatePriceCommand command,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid organizationId,
        Guid priceId,
        CancellationToken cancellationToken = default);
}
