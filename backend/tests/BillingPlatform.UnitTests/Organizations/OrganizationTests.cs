using BillingPlatform.Organizations.Domain;

namespace BillingPlatform.UnitTests.Organizations;

public sealed class OrganizationTests
{
    [Fact]
    public void Create_normalizes_bounded_settings()
    {
        var createdAt = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.FromHours(-3));

        var organization = Organization.Create(
            Guid.NewGuid(),
            "  Acme  ",
            "usd",
            "inv-demo",
            "secret",
            true,
            createdAt);

        Assert.Equal("Acme", organization.Name);
        Assert.Equal("USD", organization.DefaultCurrency);
        Assert.Equal("INV-DEMO", organization.InvoicePrefix);
        Assert.Equal(TimeSpan.Zero, organization.CreatedAt.Offset);
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("US12")]
    public void Create_rejects_invalid_currency(string currency)
    {
        Assert.ThrowsAny<ArgumentException>(() => Organization.Create(
            Guid.NewGuid(),
            "Acme",
            currency,
            "INV",
            "secret",
            true,
            DateTimeOffset.UnixEpoch));
    }
}
