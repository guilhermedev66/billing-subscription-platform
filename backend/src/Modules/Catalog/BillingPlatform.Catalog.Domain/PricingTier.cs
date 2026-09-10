namespace BillingPlatform.Catalog.Domain;

public sealed class PricingTier
{
    private PricingTier()
    {
    }

    private PricingTier(
        Guid id,
        int startingUnit,
        int? endingUnit,
        long unitAmountCents)
    {
        Id = id;
        StartingUnit = startingUnit;
        EndingUnit = endingUnit;
        UnitAmountCents = unitAmountCents;
    }

    public Guid Id { get; private set; }

    public int StartingUnit { get; private set; }

    public int? EndingUnit { get; private set; }

    public long UnitAmountCents { get; private set; }

    public static PricingTier Create(
        int startingUnit,
        int? endingUnit,
        long unitAmountCents)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(startingUnit, 1);
        if (endingUnit is < 1 || endingUnit < startingUnit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endingUnit),
                "Ending unit must be greater than or equal to the starting unit.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(unitAmountCents);

        return new PricingTier(
            Guid.NewGuid(),
            startingUnit,
            endingUnit,
            unitAmountCents);
    }
}
