using BillingPlatform.Catalog.Application;
using BillingPlatform.Catalog.Domain;
using BillingPlatform.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingPlatform.Catalog.Infrastructure;

internal sealed class ProductService(CatalogDbContext dbContext) : IProductService
{
    public async Task<ProductSummary> CreateAsync(
        Guid organizationId,
        CreateProductCommand command,
        CancellationToken cancellationToken = default)
    {
        var product = Product.Create(
            Guid.NewGuid(),
            organizationId,
            command.Name,
            command.Description,
            command.Active);

        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToSummary(product);
    }

    public async Task<IReadOnlyList<ProductSummary>> ListAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Products
            .AsNoTracking()
            .Where(product => product.OrganizationId == organizationId)
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Select(product => new ProductSummary(
                product.Id,
                product.OrganizationId,
                product.Name,
                product.Description,
                product.Active))
            .ToListAsync(cancellationToken);

    public async Task<ProductSummary?> GetAsync(
        Guid organizationId,
        Guid productId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Products
            .AsNoTracking()
            .Where(product => product.OrganizationId == organizationId)
            .Where(product => product.Id == productId)
            .Select(product => new ProductSummary(
                product.Id,
                product.OrganizationId,
                product.Name,
                product.Description,
                product.Active))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<ProductSummary?> UpdateAsync(
        Guid organizationId,
        Guid productId,
        UpdateProductCommand command,
        CancellationToken cancellationToken = default)
    {
        var product = await dbContext.Products
            .Where(product => product.OrganizationId == organizationId)
            .Where(product => product.Id == productId)
            .SingleOrDefaultAsync(cancellationToken);
        if (product is null)
        {
            return null;
        }

        product.Update(command.Name, command.Description, command.Active);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToSummary(product);
    }

    public async Task<bool> DeleteAsync(
        Guid organizationId,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var product = await dbContext.Products
            .Where(product => product.OrganizationId == organizationId)
            .Where(product => product.Id == productId)
            .SingleOrDefaultAsync(cancellationToken);
        if (product is null)
        {
            return false;
        }

        dbContext.Products.Remove(product);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static ProductSummary ToSummary(Product product) =>
        new(product.Id, product.OrganizationId, product.Name, product.Description, product.Active);
}

internal sealed class PriceService(CatalogDbContext dbContext) : IPriceService
{
    public async Task<PriceSummary?> CreateAsync(
        Guid organizationId,
        CreatePriceCommand command,
        CancellationToken cancellationToken = default)
    {
        var productExists = await dbContext.Products.AnyAsync(
            product => product.Id == command.ProductId && product.OrganizationId == organizationId,
            cancellationToken);
        if (!productExists)
        {
            return null;
        }

        var price = Price.Create(
            Guid.NewGuid(),
            command.ProductId,
            organizationId,
            command.PricingModel,
            command.Currency,
            command.BillingInterval,
            command.FlatUnitAmountCents,
            command.PerSeatUnitAmountCents,
            command.MeteredUnitAmountCents,
            command.MeteredAggregation,
            command.Tiers.Select(ToTier),
            command.TrialDays);

        dbContext.Prices.Add(price);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToSummary(price);
    }

    public async Task<IReadOnlyList<PriceSummary>> ListAsync(
        Guid organizationId,
        Guid? productId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Prices
            .AsNoTracking()
            .Where(price => price.OrganizationId == organizationId);
        if (productId is not null)
        {
            query = query.Where(price => price.ProductId == productId);
        }

        var prices = await query
            .OrderBy(price => price.ProductId)
            .ThenBy(price => price.Id)
            .Include(price => price.Tiers)
            .ToListAsync(cancellationToken);

        return prices.Select(ToSummary).ToList();
    }

    public async Task<PriceSummary?> GetAsync(
        Guid organizationId,
        Guid priceId,
        CancellationToken cancellationToken = default)
    {
        var price = await dbContext.Prices
            .AsNoTracking()
            .Where(price => price.OrganizationId == organizationId)
            .Where(price => price.Id == priceId)
            .Include(price => price.Tiers)
            .SingleOrDefaultAsync(cancellationToken);
        return price is null ? null : ToSummary(price);
    }

    public async Task<PriceSummary?> UpdateAsync(
        Guid organizationId,
        Guid priceId,
        UpdatePriceCommand command,
        CancellationToken cancellationToken = default)
    {
        var price = await dbContext.Prices
            .Where(price => price.OrganizationId == organizationId)
            .Where(price => price.Id == priceId)
            .Include(price => price.Tiers)
            .SingleOrDefaultAsync(cancellationToken);
        if (price is null)
        {
            return null;
        }

        price.Update(
            command.PricingModel,
            command.Currency,
            command.BillingInterval,
            command.FlatUnitAmountCents,
            command.PerSeatUnitAmountCents,
            command.MeteredUnitAmountCents,
            command.MeteredAggregation,
            command.Tiers.Select(ToTier),
            command.TrialDays);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToSummary(price);
    }

    public async Task<bool> DeleteAsync(
        Guid organizationId,
        Guid priceId,
        CancellationToken cancellationToken = default)
    {
        var price = await dbContext.Prices
            .Where(price => price.OrganizationId == organizationId)
            .Where(price => price.Id == priceId)
            .SingleOrDefaultAsync(cancellationToken);
        if (price is null)
        {
            return false;
        }

        dbContext.Prices.Remove(price);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static PricingTier ToTier(PricingTierInput tier) =>
        PricingTier.Create(tier.StartingUnit, tier.EndingUnit, tier.UnitAmountCents);

    private static PriceSummary ToSummary(Price price) =>
        new(
            price.Id,
            price.ProductId,
            price.OrganizationId,
            price.PricingModel,
            price.Currency,
            price.BillingInterval,
            price.FlatUnitAmountCents,
            price.PerSeatUnitAmountCents,
            price.MeteredUnitAmountCents,
            price.MeteredAggregation,
            price.Tiers
                .OrderBy(tier => tier.StartingUnit)
                .Select(tier => new PricingTierSummary(
                    tier.Id,
                    tier.StartingUnit,
                    tier.EndingUnit,
                    tier.UnitAmountCents))
                .ToList(),
            price.TrialDays);
}
