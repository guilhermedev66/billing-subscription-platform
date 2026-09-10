namespace BillingPlatform.Payments.Domain;

public sealed class PaymentSweepIdempotencyRecord
{
    private PaymentSweepIdempotencyRecord()
    {
    }

    private PaymentSweepIdempotencyRecord(
        Guid id,
        Guid organizationId,
        string idempotencyKey,
        string requestHash,
        string responseBody,
        DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        IdempotencyKey = idempotencyKey;
        RequestHash = requestHash;
        ResponseBody = responseBody;
        CreatedAt = createdAt.ToUniversalTime();
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    public string RequestHash { get; private set; } = string.Empty;

    public string ResponseBody { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static PaymentSweepIdempotencyRecord Create(
        Guid id,
        Guid organizationId,
        string idempotencyKey,
        string requestHash,
        string responseBody,
        DateTimeOffset createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(organizationId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(responseBody);
        return new PaymentSweepIdempotencyRecord(
            id, organizationId, idempotencyKey, requestHash, responseBody, createdAt);
    }
}
