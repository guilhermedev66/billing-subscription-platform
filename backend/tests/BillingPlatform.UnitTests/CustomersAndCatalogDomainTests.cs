using BillingPlatform.Catalog.Domain;
using BillingPlatform.Customers.Domain;

namespace BillingPlatform.UnitTests;

public sealed class CustomersAndCatalogDomainTests
{
    [Fact]
    public void Customer_balance_is_integer_cents_and_can_represent_credit_or_debt()
    {
        Assert.Equal(typeof(long), typeof(Customer).GetProperty(nameof(Customer.BalanceCents))!.PropertyType);

        var customer = Customer.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Acme",
            "billing@acme.example",
            -1250,
            false,
            DateTimeOffset.UtcNow);

        Assert.Equal(-1250L, customer.BalanceCents);
    }

    [Fact]
    public void Price_rejects_negative_money_amounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Price.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                PricingModel.Flat,
                "USD",
                BillingInterval.Month,
                -1,
                null,
                null,
                null,
                [],
                null));
    }

    [Fact]
    public void Metered_price_rejects_an_undefined_aggregation_value()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Price.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                PricingModel.Metered,
                "USD",
                BillingInterval.Month,
                null,
                null,
                5,
                (MeteredAggregation)99,
                [],
                null));
    }

    [Theory]
    [InlineData("a b@c.com")]
    [InlineData("@example.com")]
    [InlineData("a..b@example.com")]
    [InlineData("a@example..com")]
    [InlineData("a@-example.com")]
    [InlineData("a@example-.com")]
    [InlineData("a@example")]
    public void Customer_rejects_malformed_email_addresses(string email)
    {
        Assert.Throws<ArgumentException>(() =>
            Customer.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Acme",
                email,
                0,
                false,
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Tiered_price_rejects_zero_tiers()
    {
        Assert.Throws<ArgumentException>(() => CreateTieredPrice([]));
    }

    [Fact]
    public void Tiered_price_rejects_overlapping_tiers()
    {
        var tiers = new[]
        {
            PricingTier.Create(1, 5, 2000),
            PricingTier.Create(5, 10, 1500)
        };

        Assert.Throws<ArgumentException>(() => CreateTieredPrice(tiers));
    }

    [Fact]
    public void Tiered_price_rejects_gaps_between_tiers()
    {
        var tiers = new[]
        {
            PricingTier.Create(1, 10, 2000),
            PricingTier.Create(20, 30, 1500)
        };

        Assert.Throws<ArgumentException>(() => CreateTieredPrice(tiers));
    }

    [Fact]
    public void Tiered_price_rejects_descending_tier_bounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PricingTier.Create(10, 5, 1000));
    }

    [Fact]
    public void Tiered_price_rejects_descending_tier_order()
    {
        var tiers = new[]
        {
            PricingTier.Create(6, 20, 1500),
            PricingTier.Create(1, 5, 2000)
        };

        Assert.Throws<ArgumentException>(() => CreateTieredPrice(tiers));
    }

    [Fact]
    public void Tiered_price_accepts_ascending_non_overlapping_tiers()
    {
        var price = CreateTieredPrice(
        [
            PricingTier.Create(1, 5, 2000),
            PricingTier.Create(6, 20, 1500),
            PricingTier.Create(21, null, 1000)
        ]);

        Assert.Equal(3, price.Tiers.Count);
    }

    private static Price CreateTieredPrice(IEnumerable<PricingTier> tiers) =>
        Price.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            PricingModel.Tiered,
            "USD",
            BillingInterval.Month,
            null,
            null,
            null,
            null,
            tiers,
            null);
}
