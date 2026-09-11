namespace BillingPlatform.Webhooks.Domain;

public sealed class WebhookDeliveryAttempt
{
    private WebhookDeliveryAttempt()
    {
    }

    private WebhookDeliveryAttempt(
        Guid id,
        Guid outboxEventId,
        Guid endpointId,
        DateTimeOffset attemptedAt,
        int? statusCode,
        long durationMilliseconds,
        int attemptNumber,
        string? responseBody,
        string? error)
    {
        Id = id;
        OutboxEventId = outboxEventId;
        EndpointId = endpointId;
        AttemptedAt = attemptedAt.ToUniversalTime();
        StatusCode = statusCode;
        DurationMilliseconds = durationMilliseconds;
        AttemptNumber = attemptNumber;
        ResponseBody = responseBody;
        Error = error;
    }

    public Guid Id { get; private set; }
    public Guid OutboxEventId { get; private set; }
    public Guid EndpointId { get; private set; }
    public DateTimeOffset AttemptedAt { get; private set; }
    public int? StatusCode { get; private set; }
    public long DurationMilliseconds { get; private set; }
    public int AttemptNumber { get; private set; }
    public string? ResponseBody { get; private set; }
    public string? Error { get; private set; }

    public static WebhookDeliveryAttempt Create(
        Guid id,
        Guid outboxEventId,
        Guid endpointId,
        DateTimeOffset attemptedAt,
        int? statusCode,
        long durationMilliseconds,
        int attemptNumber,
        string? responseBody,
        string? error) =>
        new(id, outboxEventId, endpointId, attemptedAt, statusCode, durationMilliseconds,
            attemptNumber, responseBody, error);
}
