namespace BillingPlatform.Catalog.Domain;

public sealed class Price
{
    private readonly List<PricingTier> tiers = [];

    private Price()
    {
    }

    private Price(
        Guid id,
        Guid productId,
        Guid organizationId,
        PricingModel pricingModel,
        string currency,
        BillingInterval billingInterval,
        long? flatUnitAmountCents,
        long? perSeatUnitAmountCents,
        long? meteredUnitAmountCents,
        MeteredAggregation? meteredAggregation,
        IEnumerable<PricingTier> pricingTiers,
        int? trialDays)
    {
        Id = id;
        ProductId = productId;
        OrganizationId = organizationId;
        PricingModel = pricingModel;
        Currency = currency;
        BillingInterval = billingInterval;
        FlatUnitAmountCents = flatUnitAmountCents;
        PerSeatUnitAmountCents = perSeatUnitAmountCents;
        MeteredUnitAmountCents = meteredUnitAmountCents;
        MeteredAggregation = meteredAggregation;
        this.tiers.AddRange(pricingTiers);
        TrialDays = trialDays;
        Version = 1;
    }

    public Guid Id { get; private set; }

    public Guid ProductId { get; private set; }

    public Guid OrganizationId { get; private set; }

    public PricingModel PricingModel { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public BillingInterval BillingInterval { get; private set; }

    public long? FlatUnitAmountCents { get; private set; }

    public long? PerSeatUnitAmountCents { get; private set; }

    public long? MeteredUnitAmountCents { get; private set; }

    public MeteredAggregation? MeteredAggregation { get; private set; }

    public IReadOnlyCollection<PricingTier> Tiers => tiers.AsReadOnly();

    public int? TrialDays { get; private set; }

    public int Version { get; private set; }

    public static Price Create(
        Guid id,
        Guid productId,
        Guid organizationId,
        PricingModel pricingModel,
        string currency,
        BillingInterval billingInterval,
        long? flatUnitAmountCents,
        long? perSeatUnitAmountCents,
        long? meteredUnitAmountCents,
        MeteredAggregation? meteredAggregation,
        IEnumerable<PricingTier>? pricingTiers,
        int? trialDays)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(productId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(organizationId, Guid.Empty);

        var normalizedTiers = pricingTiers?.ToList() ?? [];
        Validate(
            pricingModel,
            currency,
            billingInterval,
            flatUnitAmountCents,
            perSeatUnitAmountCents,
            meteredUnitAmountCents,
            meteredAggregation,
            normalizedTiers,
            trialDays);

        return new Price(
            id,
            productId,
            organizationId,
            pricingModel,
            NormalizeCurrency(currency),
            billingInterval,
            flatUnitAmountCents,
            perSeatUnitAmountCents,
            meteredUnitAmountCents,
            meteredAggregation,
            normalizedTiers,
            trialDays);
    }

    public void Update(
        PricingModel pricingModel,
        string currency,
        BillingInterval billingInterval,
        long? flatUnitAmountCents,
        long? perSeatUnitAmountCents,
        long? meteredUnitAmountCents,
        MeteredAggregation? meteredAggregation,
        IEnumerable<PricingTier>? pricingTiers,
        int? trialDays)
    {
        var normalizedTiers = pricingTiers?.ToList() ?? [];
        Validate(
            pricingModel,
            currency,
            billingInterval,
            flatUnitAmountCents,
            perSeatUnitAmountCents,
            meteredUnitAmountCents,
            meteredAggregation,
            normalizedTiers,
            trialDays);

        PricingModel = pricingModel;
        Currency = NormalizeCurrency(currency);
        BillingInterval = billingInterval;
        FlatUnitAmountCents = flatUnitAmountCents;
        PerSeatUnitAmountCents = perSeatUnitAmountCents;
        MeteredUnitAmountCents = meteredUnitAmountCents;
        MeteredAggregation = meteredAggregation;
        tiers.Clear();
        tiers.AddRange(normalizedTiers);
        TrialDays = trialDays;
        checked { Version++; }
    }

    private static void Validate(
        PricingModel pricingModel,
        string currency,
        BillingInterval billingInterval,
        long? flatUnitAmountCents,
        long? perSeatUnitAmountCents,
        long? meteredUnitAmountCents,
        MeteredAggregation? meteredAggregation,
        IReadOnlyList<PricingTier> pricingTiers,
        int? trialDays)
    {
        if (!Enum.IsDefined(pricingModel))
        {
            throw new ArgumentOutOfRangeException(nameof(pricingModel));
        }

        if (!Enum.IsDefined(billingInterval))
        {
            throw new ArgumentOutOfRangeException(nameof(billingInterval));
        }

        NormalizeCurrency(currency);
        if (trialDays is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(trialDays), "Trial days cannot be negative.");
        }

        if (pricingModel == PricingModel.Metered &&
            (meteredAggregation is null || !Enum.IsDefined(meteredAggregation.Value)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(meteredAggregation),
                "Metered aggregation must be Sum, Max, or Last.");
        }

        ValidateAmount(flatUnitAmountCents, nameof(flatUnitAmountCents));
        ValidateAmount(perSeatUnitAmountCents, nameof(perSeatUnitAmountCents));
        ValidateAmount(meteredUnitAmountCents, nameof(meteredUnitAmountCents));

        switch (pricingModel)
        {
            case PricingModel.Flat when flatUnitAmountCents is null ||
                perSeatUnitAmountCents is not null ||
                meteredUnitAmountCents is not null ||
                meteredAggregation is not null ||
                pricingTiers.Count != 0:
                throw new ArgumentException(
                    "Flat pricing requires only flat unit amount cents.",
                    nameof(flatUnitAmountCents));
            case PricingModel.PerSeat when perSeatUnitAmountCents is null ||
                flatUnitAmountCents is not null ||
                meteredUnitAmountCents is not null ||
                meteredAggregation is not null ||
                pricingTiers.Count != 0:
                throw new ArgumentException(
                    "Per-seat pricing requires only per-seat unit amount cents.",
                    nameof(perSeatUnitAmountCents));
            case PricingModel.Metered when meteredUnitAmountCents is null ||
                meteredAggregation is null ||
                flatUnitAmountCents is not null ||
                perSeatUnitAmountCents is not null ||
                pricingTiers.Count != 0:
                throw new ArgumentException(
                    "Metered pricing requires metered unit amount cents and aggregation.",
                    nameof(meteredUnitAmountCents));
            case PricingModel.Tiered when flatUnitAmountCents is not null ||
                perSeatUnitAmountCents is not null ||
                meteredUnitAmountCents is not null ||
                meteredAggregation is not null ||
                pricingTiers.Count == 0:
                throw new ArgumentException(
                    "Tiered pricing requires at least one tier and no single unit amount.",
                    nameof(pricingTiers));
        }

        if (pricingModel == PricingModel.Tiered)
        {
            ValidateTiers(pricingTiers);
        }
    }

    private static void ValidateTiers(IReadOnlyList<PricingTier> pricingTiers)
    {
        var orderedTiers = pricingTiers.ToArray();

        if (orderedTiers[0].StartingUnit != 1)
        {
            throw new ArgumentException("Tiered pricing must start at unit 1.", nameof(pricingTiers));
        }

        for (var index = 0; index < orderedTiers.Length; index++)
        {
            var current = orderedTiers[index];
            if (current.EndingUnit is not null && current.EndingUnit < current.StartingUnit)
            {
                throw new ArgumentException(
                    "Tier ending units must not be below their starting units.",
                    nameof(pricingTiers));
            }

            if (index == 0)
            {
                continue;
            }

            var previous = orderedTiers[index - 1];
            if (previous.EndingUnit is null ||
                previous.EndingUnit == int.MaxValue ||
                current.StartingUnit != previous.EndingUnit.Value + 1)
            {
                throw new ArgumentException(
                    "Pricing tiers must be ascending, contiguous, and non-overlapping.",
                    nameof(pricingTiers));
            }
        }
    }

    private static void ValidateAmount(long? amount, string parameterName)
    {
        if (amount is < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Money amounts cannot be negative.");
        }
    }

    private static string NormalizeCurrency(string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        var normalizedCurrency = currency.Trim().ToUpperInvariant();

        if (normalizedCurrency.Length != 3 || !normalizedCurrency.All(char.IsAsciiLetter))
        {
            throw new ArgumentException(
                "Currency must be a three-letter ISO currency code.",
                nameof(currency));
        }

        return normalizedCurrency;
    }
}
