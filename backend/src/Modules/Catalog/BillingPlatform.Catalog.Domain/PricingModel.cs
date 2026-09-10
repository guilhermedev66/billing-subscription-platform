namespace BillingPlatform.Catalog.Domain;

public enum PricingModel
{
    Flat,
    PerSeat,
    Tiered,
    Metered
}

public enum BillingInterval
{
    Month,
    Year
}

public enum MeteredAggregation
{
    Sum,
    Max,
    Last
}
