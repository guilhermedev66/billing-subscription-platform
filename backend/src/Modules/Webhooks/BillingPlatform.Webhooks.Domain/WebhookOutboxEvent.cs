namespace BillingPlatform.Webhooks.Domain;

public sealed class WebhookOutboxEvent
{
    private WebhookOutboxEvent()
    {
    }

    private WebhookOutboxEvent(
        Guid id,
        Guid organizationId,
        string eventType,
        string aggregateType,
        Guid aggregateId,
        byte[] rawBody,
        DateTimeOffset occurredAt)
    {
        Id = id;
        OrganizationId = organizationId;
        EventType = eventType;
        AggregateType = aggregateType;
        AggregateId = aggregateId;
        RawBody = rawBody;
        OccurredAt = occurredAt.ToUniversalTime();
        AvailableAt = OccurredAt;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string AggregateType { get; private set; } = string.Empty;
    public Guid AggregateId { get; private set; }
    public byte[] RawBody { get; private set; } = [];
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset AvailableAt { get; private set; }
    public DateTimeOffset? DispatchedAt { get; private set; }
    public Guid? DispatchLeaseId { get; private set; }
    public DateTimeOffset? DispatchLeaseUntil { get; private set; }

    public static WebhookOutboxEvent Create(
        Guid id,
        Guid organizationId,
        string eventType,
        string aggregateType,
        Guid aggregateId,
        byte[] rawBody,
        DateTimeOffset occurredAt) =>
        new(id, organizationId, eventType, aggregateType, aggregateId, rawBody, occurredAt);

    public void MarkDispatched(DateTimeOffset at) => DispatchedAt = at.ToUniversalTime();

    public void ScheduleRetry(DateTimeOffset availableAt) =>
        AvailableAt = availableAt.ToUniversalTime();

    public void Claim(Guid leaseId, DateTimeOffset leaseUntil)
    {
        DispatchLeaseId = leaseId;
        DispatchLeaseUntil = leaseUntil.ToUniversalTime();
    }

    public void ReleaseLease()
    {
        DispatchLeaseId = null;
        DispatchLeaseUntil = null;
    }
}
