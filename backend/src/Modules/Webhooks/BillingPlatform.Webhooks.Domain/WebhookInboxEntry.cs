namespace BillingPlatform.Webhooks.Domain;

public sealed class WebhookInboxEntry
{
    private WebhookInboxEntry()
    {
    }

    private WebhookInboxEntry(
        Guid id,
        Guid organizationId,
        Guid eventId,
        string eventType,
        byte[] rawBody,
        int sequence,
        DateTimeOffset receivedAt)
    {
        Id = id;
        OrganizationId = organizationId;
        EventId = eventId;
        EventType = eventType;
        RawBody = rawBody;
        Sequence = sequence;
        ReceivedAt = receivedAt.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public byte[] RawBody { get; private set; } = [];
    public int Sequence { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }

    public static WebhookInboxEntry Create(
        Guid id,
        Guid organizationId,
        Guid eventId,
        string eventType,
        byte[] rawBody,
        int sequence,
        DateTimeOffset receivedAt) =>
        new(id, organizationId, eventId, eventType, rawBody, sequence, receivedAt);
}
