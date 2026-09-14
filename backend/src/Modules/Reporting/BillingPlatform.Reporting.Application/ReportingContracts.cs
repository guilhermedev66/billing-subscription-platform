namespace BillingPlatform.Reporting.Application;

public sealed record RevenueTotals(
    string Currency,
    long ArrCents,
    long MrrCents,
    long AtRiskArrCents,
    long AtRiskMrrCents,
    int RevenueBearingSubscriptions,
    int MeteredExcludedSubscriptions);

public sealed record WaterfallTotals(
    string Currency,
    long StartingArrCents,
    long NewArrCents,
    long ExpansionArrCents,
    long ReactivationArrCents,
    long ContractionArrCents,
    long ChurnArrCents,
    long EndingArrCents,
    long StartingMrrCents,
    long NewMrrCents,
    long ExpansionMrrCents,
    long ReactivationMrrCents,
    long ContractionMrrCents,
    long ChurnMrrCents,
    long EndingMrrCents);

public sealed record ReportingEventDetail(
    Guid SourceEventId,
    string SourceType,
    Guid? SubscriptionId,
    Guid? PriceId,
    int? SubscriptionVersion,
    int? PriceVersion,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    System.Text.Json.JsonElement Payload);

public interface IReportingService
{
    Task<IReadOnlyList<RevenueTotals>> CurrentAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WaterfallTotals>> WaterfallAsync(
        Guid organizationId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<ReportingEventDetail?> GetEventAsync(
        Guid organizationId,
        Guid sourceEventId,
        CancellationToken cancellationToken = default);

    Task RebuildAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
