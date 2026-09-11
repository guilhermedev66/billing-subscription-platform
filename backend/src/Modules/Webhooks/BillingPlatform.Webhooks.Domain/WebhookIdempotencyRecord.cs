namespace BillingPlatform.Webhooks.Domain;

public sealed class WebhookIdempotencyRecord
{
    private WebhookIdempotencyRecord()
    {
    }

    private WebhookIdempotencyRecord(
        Guid id,
        Guid organizationId,
        string operation,
        string idempotencyKey,
        string requestHash,
        int responseStatusCode,
        string responseBody,
        string? responseLocation,
        DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        Operation = operation;
        IdempotencyKey = idempotencyKey;
        RequestHash = requestHash;
        ResponseStatusCode = responseStatusCode;
        ResponseBody = responseBody;
        ResponseLocation = responseLocation;
        CreatedAt = createdAt.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public int ResponseStatusCode { get; private set; }
    public string ResponseBody { get; private set; } = string.Empty;
    public string? ResponseLocation { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static WebhookIdempotencyRecord Create(
        Guid id,
        Guid organizationId,
        string operation,
        string idempotencyKey,
        string requestHash,
        int responseStatusCode,
        string responseBody,
        string? responseLocation,
        DateTimeOffset createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(organizationId, Guid.Empty);
        if (string.IsNullOrWhiteSpace(operation) || operation.Length > 64)
        {
            throw new ArgumentException("Idempotency operation is required and must be short.", nameof(operation));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 255)
        {
            throw new ArgumentException(
                "Idempotency-Key is required and must be at most 255 characters.",
                nameof(idempotencyKey));
        }

        if (string.IsNullOrWhiteSpace(requestHash) || requestHash.Length > 128)
        {
            throw new ArgumentException("Idempotency request hash is required.", nameof(requestHash));
        }

        if (responseStatusCode is < 200 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(responseStatusCode));
        }

        if (string.IsNullOrEmpty(responseBody))
        {
            throw new ArgumentException("Idempotency response body is required.", nameof(responseBody));
        }

        return new WebhookIdempotencyRecord(
            id,
            organizationId,
            operation,
            idempotencyKey,
            requestHash,
            responseStatusCode,
            responseBody,
            responseLocation,
            createdAt);
    }
}
