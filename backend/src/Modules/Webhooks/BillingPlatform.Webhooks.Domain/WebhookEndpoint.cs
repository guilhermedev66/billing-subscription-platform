namespace BillingPlatform.Webhooks.Domain;

public sealed class WebhookEndpoint
{
    private WebhookEndpoint()
    {
    }

    private WebhookEndpoint(
        Guid id,
        Guid organizationId,
        string url,
        string secret,
        IReadOnlyList<string> eventTypes,
        DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        Url = url;
        Secret = secret;
        EventTypesJson = System.Text.Json.JsonSerializer.Serialize(eventTypes);
        Active = true;
        CreatedAt = createdAt.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Url { get; private set; } = string.Empty;
    public string Secret { get; private set; } = string.Empty;
    public string EventTypesJson { get; private set; } = "[]";
    public bool Active { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static WebhookEndpoint Register(
        Guid id,
        Guid organizationId,
        string url,
        string secret,
        IReadOnlyList<string>? eventTypes,
        DateTimeOffset createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(organizationId, Guid.Empty);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            parsed.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Webhook URL must be an absolute HTTP(S) URL.", nameof(url));
        }

        if (string.IsNullOrWhiteSpace(secret) || secret.Length is < 32 or > 256)
        {
            throw new ArgumentException(
                "Webhook secret is required and must be between 32 and 256 characters.",
                nameof(secret));
        }

        if (eventTypes is null)
        {
            throw new ArgumentException("Webhook event types are required.", nameof(eventTypes));
        }

        var normalizedEvents = eventTypes
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (normalizedEvents.Length == 0)
        {
            throw new ArgumentException("At least one event type must be subscribed.", nameof(eventTypes));
        }

        return new WebhookEndpoint(id, organizationId, parsed.ToString(), secret, normalizedEvents, createdAt);
    }

    public IReadOnlyList<string> EventTypes =>
        System.Text.Json.JsonSerializer.Deserialize<string[]>(EventTypesJson) ?? [];

    public bool SubscribesTo(string eventType) =>
        EventTypes.Contains(eventType, StringComparer.Ordinal);

    public void Deactivate() => Active = false;
}
