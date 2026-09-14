using BillingPlatform.Catalog.Application;
using BillingPlatform.Subscriptions.Application;

namespace BillingPlatform.Api;

internal sealed class CatalogSubscriptionPriceReader(IPriceService priceService)
    : ISubscriptionPriceReader
{
    public async Task<SubscriptionPrice?> GetAsync(
        Guid organizationId,
        Guid priceId,
        CancellationToken cancellationToken = default)
    {
        var price = await priceService.GetAsync(
            organizationId,
            priceId,
            cancellationToken);
        return price is null
            ? null
            : new SubscriptionPrice(
                price.Id,
                price.PricingModel.ToString(),
                price.Currency,
                price.BillingInterval.ToString(),
                price.FlatUnitAmountCents,
                price.PerSeatUnitAmountCents,
                price.MeteredUnitAmountCents,
                price.Tiers
                    .Select(tier => new SubscriptionPriceTier(
                        tier.StartingUnit,
                        tier.EndingUnit,
                        tier.UnitAmountCents))
                    .ToList(),
                price.TrialDays,
                price.Version);
    }
}
