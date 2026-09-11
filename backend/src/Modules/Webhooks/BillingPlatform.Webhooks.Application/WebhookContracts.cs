using BillingPlatform.SimulationClock.Application;
using BillingPlatform.Webhooks.Domain;

namespace BillingPlatform.Webhooks.Application;

public sealed class WebhookSignatureException(string message) : Exception(message);

public sealed class WebhookDeliveryException(string message) : Exception(message);

public sealed record RegisterWebhookEndpointCommand(
    string Url,
    string Secret,
    IReadOnlyList<string>? EventTypes);

public sealed class WebhookIdempotencyConflictException()
    : Exception("The Idempotency-Key was already used with a different webhook request.");

public sealed record WebhookEndpointSummary(
    Guid Id,
    Guid OrganizationId,
    string Url,
    IReadOnlyList<string> EventTypes,
    bool Active,
    DateTimeOffset CreatedAt,
    string? Secret = null);

public sealed record WebhookEndpointMutationResult(
    WebhookEndpointSummary Value,
    int StatusCode,
    string? Location,
    bool Replayed,
    string ResponseBody);

public sealed record WebhookEventSummary(
    Guid Id,
    Guid OrganizationId,
    string EventType,
    string AggregateType,
    Guid AggregateId,
    DateTimeOffset OccurredAt,
    DateTimeOffset? DispatchedAt,
    int DeliveryAttemptCount,
    byte[] RawBody);

public sealed record WebhookDeliverySummary(
    Guid Id,
    Guid OutboxEventId,
    Guid EndpointId,
    DateTimeOffset AttemptedAt,
    int? StatusCode,
    long DurationMilliseconds,
    int AttemptNumber,
    int RetryCount,
    string? ResponseBody,
    string? Error);

public sealed record InboundWebhookResult(
    Guid EventId,
    bool Duplicate,
    bool Applied,
    string EventType,
    int Sequence);

public sealed record WebhookDispatchResult(
    int Considered,
    int Delivered,
    int Failed,
    int Skipped,
    IReadOnlyList<WebhookDeliverySummary> Attempts);

public sealed record OutboundWebhookEvent(
    Guid Id,
    string EventType,
    string AggregateType,
    Guid AggregateId,
    DateTimeOffset OccurredAt,
    object Data);

public interface IWebhookEventWriter
{
    Task<Guid> EnqueueAsync(
        Guid organizationId,
        string eventType,
        string aggregateType,
        Guid aggregateId,
        object data,
        CancellationToken cancellationToken = default);
}

public interface IWebhookService : IWebhookEventWriter
{
    Task<WebhookEndpointMutationResult> RegisterEndpointAsync(
        Guid organizationId,
        RegisterWebhookEndpointCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebhookEndpointSummary>> ListEndpointsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebhookEventSummary>> ListEventsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebhookDeliverySummary>?> ListDeliveriesAsync(
        Guid organizationId,
        Guid eventId,
        CancellationToken cancellationToken = default);

    Task<InboundWebhookResult> IngestAsync(
        Guid organizationId,
        Guid endpointId,
        byte[] rawBody,
        string signature,
        CancellationToken cancellationToken = default);

    Task<WebhookDispatchResult> DispatchAsync(
        Guid? organizationId = null,
        CancellationToken cancellationToken = default);
}

public interface IWebhookTransactionCoordinator
{
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default);
}

public static class WebhookSignature
{
    public const int ReplayWindowSeconds = 300;

    public static string CreateHeader(string secret, DateTimeOffset timestamp, ReadOnlySpan<byte> rawBody) =>
        CreateHeader(System.Text.Encoding.UTF8.GetBytes(secret), timestamp, rawBody);

    public static string CreateHeader(byte[] secret, DateTimeOffset timestamp, ReadOnlySpan<byte> rawBody)
    {
        var unixTimestamp = timestamp.ToUnixTimeSeconds();
        var prefix = System.Text.Encoding.UTF8.GetBytes($"{unixTimestamp}.");
        var preimage = new byte[prefix.Length + rawBody.Length];
        prefix.CopyTo(preimage, 0);
        rawBody.CopyTo(preimage.AsSpan(prefix.Length));
        var digest = System.Security.Cryptography.HMACSHA256.HashData(secret, preimage);
        return $"t={unixTimestamp},v1={Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    public static bool Verify(
        string signature,
        string secret,
        byte[] rawBody,
        DateTimeOffset now,
        out long timestamp)
    {
        timestamp = 0;
        Dictionary<string, string> values;
        try
        {
            values = signature.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .Where(part => part.Length == 2)
                .ToDictionary(part => part[0], part => part[1], StringComparer.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        if (!values.TryGetValue("t", out var timestampText) ||
            !long.TryParse(timestampText, out timestamp) ||
            !values.TryGetValue("v1", out var supplied))
        {
            return false;
        }

        DateTimeOffset signedAt;
        try
        {
            signedAt = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        if (Math.Abs((now - signedAt).TotalSeconds) > ReplayWindowSeconds)
        {
            return false;
        }

        var prefix = System.Text.Encoding.UTF8.GetBytes($"{timestamp}.");
        var preimage = new byte[prefix.Length + rawBody.Length];
        prefix.CopyTo(preimage, 0);
        rawBody.CopyTo(preimage.AsSpan(prefix.Length));
        var expected = System.Security.Cryptography.HMACSHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(secret), preimage);
        byte[] provided;
        try
        {
            provided = Convert.FromHexString(supplied);
        }
        catch (FormatException)
        {
            return false;
        }

        return provided.Length == expected.Length &&
               System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(provided, expected);
    }
}
