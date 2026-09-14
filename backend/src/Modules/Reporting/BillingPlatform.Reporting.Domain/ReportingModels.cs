namespace BillingPlatform.Reporting.Domain;

public sealed class ReportingSourceEvent
{
    public Guid Id { get; set; }
    public long IngestOrder { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid SourceEventId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public Guid? SubscriptionId { get; set; }
    public Guid? PriceId { get; set; }
    public int? SubscriptionVersion { get; set; }
    public int? PriceVersion { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
}

public sealed class SubscriptionRevenueSnapshot
{
    public Guid OrganizationId { get; set; }
    public Guid SubscriptionId { get; set; }
    public Guid PriceId { get; set; }
    public int PriceVersion { get; set; }
    public int SubscriptionVersion { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PricingModel { get; set; } = string.Empty;
    public int? SeatCount { get; set; }
    public long AnnualizedFixedCents { get; set; }
    public bool MeteredExcluded { get; set; }
    public bool EverRevenueBearing { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }
    public Guid LastSourceEventId { get; set; }
}

public sealed class MrrMovement
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid SubscriptionId { get; set; }
    public Guid SourceEventId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public long BeforeAnnualizedCents { get; set; }
    public long AfterAnnualizedCents { get; set; }
    public long DeltaAnnualizedCents { get; set; }
}
