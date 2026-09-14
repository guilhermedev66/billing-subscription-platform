using BillingPlatform.Catalog.Application;
using BillingPlatform.Catalog.Infrastructure.Persistence;
using BillingPlatform.Reporting.Infrastructure.Persistence;
using BillingPlatform.SimulationClock.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BillingPlatform.Api;

internal sealed class PriceMutationCoordinator(
    CatalogDbContext catalogDbContext,
    ReportingDbContext reportingDbContext,
    IPriceService priceService,
    IPriceMutationEventWriter priceMutationEventWriter,
    IVirtualClock clock) : IPriceMutationCoordinator
{
    public async Task<PriceSummary?> UpdateAsync(
        Guid organizationId,
        Guid priceId,
        UpdatePriceCommand command,
        CancellationToken cancellationToken = default)
    {
        reportingDbContext.Database.SetDbConnection(
            catalogDbContext.Database.GetDbConnection(), contextOwnsConnection: false);
        await using var transaction = await catalogDbContext.Database.BeginTransactionAsync(cancellationToken);
        await using var reportingTransaction = await reportingDbContext.Database.UseTransactionAsync(
            transaction.GetDbTransaction(), cancellationToken);
        try
        {
            var before = await priceService.GetAsync(organizationId, priceId, cancellationToken);
            if (before is null)
            {
                return null;
            }

            if (!string.Equals(before.Currency, command.Currency?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                await reportingDbContext.SubscriptionSnapshots.AsNoTracking().AnyAsync(
                    item => item.OrganizationId == organizationId && item.PriceId == priceId,
                    cancellationToken))
            {
                throw new ArgumentException(
                    "A price used by subscriptions cannot change currency.", nameof(command));
            }

            var after = await priceService.UpdateAsync(
                organizationId, priceId, command, cancellationToken)
                ?? throw new InvalidOperationException("The price disappeared during its update.");
            await priceMutationEventWriter.AppendAsync(new CatalogPriceMutationEvent(
                Guid.NewGuid(), organizationId, priceId, clock.Now.ToUniversalTime(),
                ToTerms(before), ToTerms(after)), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return after;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            catalogDbContext.ChangeTracker.Clear();
            reportingDbContext.ChangeTracker.Clear();
            throw new PriceConcurrencyException();
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            catalogDbContext.ChangeTracker.Clear();
            reportingDbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private static CatalogPriceTerms ToTerms(PriceSummary price) =>
        new(price.Id, price.Version, price.Currency,
            price.PricingModel.ToString(), price.BillingInterval.ToString(),
            price.FlatUnitAmountCents, price.PerSeatUnitAmountCents,
            price.Tiers.Select(tier => new PricingTierInput(
                tier.StartingUnit, tier.EndingUnit, tier.UnitAmountCents)).ToList());
}
