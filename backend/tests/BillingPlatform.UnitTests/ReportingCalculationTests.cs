using BillingPlatform.Reporting.Domain;

namespace BillingPlatform.UnitTests;

public sealed class ReportingCalculationTests
{
    [Fact]
    public void Annualization_matches_each_committed_pricing_model()
    {
        Assert.Equal(14_388L, RevenueCalculation.AnnualizedFixedCents(
            "Active", null, Price("Flat", "Month", flat: 1199)));
        Assert.Equal(10_000L, RevenueCalculation.AnnualizedFixedCents(
            "PastDue", 4, Price("PerSeat", "Year", perSeat: 2500)));
        Assert.Equal(6_000L, RevenueCalculation.AnnualizedFixedCents(
            "Active", 3, Price("Tiered", "Month", tiers:
            [new RevenueTier(1, 2, 100), new RevenueTier(3, null, 300)])));
        Assert.Equal(0L, RevenueCalculation.AnnualizedFixedCents(
            "Active", null, Price("Metered", "Month")));
    }

    [Theory]
    [InlineData("Trialing")]
    [InlineData("Paused")]
    [InlineData("Unpaid")]
    [InlineData("Canceled")]
    public void Ineligible_statuses_have_no_committed_run_rate(string status)
    {
        Assert.Equal(0L, RevenueCalculation.AnnualizedFixedCents(
            status, null, Price("Flat", "Month", flat: 2000)));
    }

    [Fact]
    public void Waterfall_classifies_full_contract_deltas_and_reactivation()
    {
        Assert.Equal(("New", "first_revenue"),
            RevenueCalculation.Movement(0, 12_000, false, "Active", "Flat"));
        Assert.Equal(("Expansion", "plan_or_price_increase"),
            RevenueCalculation.Movement(12_000, 18_000, true, "Active", "Flat"));
        Assert.Equal(("Contraction", "plan_or_price_decrease"),
            RevenueCalculation.Movement(18_000, 15_000, true, "Active", "Flat"));
        Assert.Equal(("Contraction", "moved_to_metered"),
            RevenueCalculation.Movement(15_000, 0, true, "Active", "Metered"));
        Assert.Equal(("Expansion", "reactivation"),
            RevenueCalculation.Movement(0, 15_000, true, "Active", "Flat"));
        Assert.Equal(("Churn", "canceled"),
            RevenueCalculation.Movement(15_000, 0, true, "Canceled", "Flat"));
        Assert.Null(RevenueCalculation.Movement(15_000, 15_000, true, "PastDue", "Flat"));
    }

    [Fact]
    public void Monthly_display_rounds_only_after_annualized_amounts_are_summed()
    {
        Assert.Equal(1L, RevenueCalculation.RoundedMonthlyCents(6));
        Assert.Equal(1L, RevenueCalculation.RoundedMonthlyCents(5 + 5));
        Assert.Equal(0L, RevenueCalculation.RoundedMonthlyCents(5));
    }

    private static RevenuePrice Price(
        string model,
        string interval,
        long? flat = null,
        long? perSeat = null,
        IReadOnlyList<RevenueTier>? tiers = null) =>
        new(Guid.NewGuid(), 1, "USD", model, interval, flat, perSeat, tiers ?? []);
}
