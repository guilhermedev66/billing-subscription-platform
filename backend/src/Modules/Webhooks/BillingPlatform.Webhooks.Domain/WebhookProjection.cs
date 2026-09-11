namespace BillingPlatform.Webhooks.Domain;

public sealed class WebhookProjection
{
    private WebhookProjection()
    {
    }

    private WebhookProjection(
        Guid id,
        Guid organizationId,
        Guid aggregateId,
        string aggregateType,
        int sequence,
        string eventType,
        byte[] rawBody,
        DateTimeOffset updatedAt)
    {
        Id = id;
        OrganizationId = organizationId;
        AggregateId = aggregateId;
        AggregateType = aggregateType;
        Sequence = sequence;
        EventType = eventType;
        RawBody = rawBody;
        UpdatedAt = updatedAt.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid AggregateId { get; private set; }
    public string AggregateType { get; private set; } = string.Empty;
    public int Sequence { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public byte[] RawBody { get; private set; } = [];
    public DateTimeOffset UpdatedAt { get; private set; }

    public static WebhookProjection Create(
        Guid id,
        Guid organizationId,
        Guid aggregateId,
        string aggregateType,
        int sequence,
        string eventType,
        byte[] rawBody,
        DateTimeOffset updatedAt) =>
        new(id, organizationId, aggregateId, aggregateType, sequence, eventType, rawBody, updatedAt);

    public void Apply(
        int sequence,
        string eventType,
        byte[] rawBody,
        DateTimeOffset updatedAt)
    {
        if (sequence < Sequence)
        {
            return;
        }

        Sequence = sequence;
        EventType = eventType;
        RawBody = rawBody;
        UpdatedAt = updatedAt.ToUniversalTime();
    }
}
