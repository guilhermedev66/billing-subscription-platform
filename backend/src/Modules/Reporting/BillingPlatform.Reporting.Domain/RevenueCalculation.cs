namespace BillingPlatform.Reporting.Domain;

public sealed record RevenueTier(int StartingUnit, int? EndingUnit, long UnitAmountCents);

public sealed record RevenuePrice(
    Guid PriceId,
    int Version,
    string Currency,
    string PricingModel,
    string BillingInterval,
    long? FlatUnitAmountCents,
    long? PerSeatUnitAmountCents,
    IReadOnlyList<RevenueTier> Tiers);

public static class RevenueCalculation
{
    public static long AnnualizedFixedCents(string status, int? seatCount, RevenuePrice price)
    {
        if (status is "Trialing" or "Paused" or "Unpaid" or "Canceled")
        {
            return 0;
        }

        if (status is not ("Active" or "PastDue"))
        {
            throw new ArgumentException("Unknown subscription status.", nameof(status));
        }

        var intervalAmount = price.PricingModel switch
        {
            "Flat" => price.FlatUnitAmountCents
                ?? throw new ArgumentException("Flat price is missing its amount."),
            "PerSeat" => checked((price.PerSeatUnitAmountCents
                ?? throw new ArgumentException("Per-seat price is missing its amount."))
                * RequireSeats(seatCount)),
            "Tiered" => TieredAmount(price.Tiers, RequireSeats(seatCount)),
            "Metered" => 0,
            _ => throw new ArgumentException("Unknown pricing model.")
        };

        return price.BillingInterval switch
        {
            "Month" => checked(intervalAmount * 12),
            "Year" => intervalAmount,
            _ => throw new ArgumentException("Unknown billing interval.")
        };
    }

    public static (string Kind, string Reason)? Movement(
        long before,
        long after,
        bool everRevenueBearing,
        string afterStatus,
        string afterPricingModel)
    {
        if (before == after)
        {
            return null;
        }

        if (before == 0)
        {
            return everRevenueBearing
                ? ("Expansion", "reactivation")
                : ("New", "first_revenue");
        }

        if (after > before)
        {
            return ("Expansion", "plan_or_price_increase");
        }

        if (after > 0)
        {
            return ("Contraction", "plan_or_price_decrease");
        }

        if (afterStatus is "Active" or "PastDue" && afterPricingModel == "Metered")
        {
            return ("Contraction", "moved_to_metered");
        }

        return ("Churn", afterStatus.ToLowerInvariant());
    }

    // The API rounds once after summing ARR. Nothing fractional is persisted.
    public static long RoundedMonthlyCents(long annualizedCents) =>
        checked((long)Math.Round(annualizedCents / 12m, 0, MidpointRounding.AwayFromZero));

    private static int RequireSeats(int? seats) => seats is > 0
        ? seats.Value
        : throw new ArgumentException("A positive seat count is required.");

    private static long TieredAmount(IReadOnlyList<RevenueTier> tiers, int seats)
    {
        long total = 0;
        var coveredThrough = 0;
        foreach (var tier in tiers.OrderBy(tier => tier.StartingUnit))
        {
            if (tier.StartingUnit > seats)
            {
                break;
            }

            var end = Math.Min(tier.EndingUnit ?? seats, seats);
            if (end >= tier.StartingUnit)
            {
                total = checked(total + checked((long)(end - tier.StartingUnit + 1) * tier.UnitAmountCents));
                coveredThrough = end;
            }
        }

        if (coveredThrough != seats)
        {
            throw new ArgumentException("The tiered price does not cover the seat count.");
        }

        return total;
    }
}
