using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BillingPlatform.SimulationClock.Application;
using BillingPlatform.Webhooks.Application;
using BillingPlatform.Webhooks.Domain;
using BillingPlatform.Webhooks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BillingPlatform.Webhooks.Infrastructure;

internal sealed class WebhookService(
    WebhooksDbContext dbContext,
    IVirtualClock clock,
    IHttpClientFactory httpClientFactory,
    IHostEnvironment environment,
    IWebhookDestinationResolver destinationResolver) : IWebhookService
{
    private const string RegisterEndpointOperation = "register-endpoint";
    private const string IdempotencyConstraintName =
        "ux_webhook_idempotency_organization_operation_key";
    private static readonly SemaphoreSlim DispatchGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WebhookEndpointMutationResult> RegisterEndpointAsync(
        Guid organizationId,
        RegisterWebhookEndpointCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeIdempotencyKey(idempotencyKey);
        var requestHash = HashRegistrationRequest(command);
        var replay = await FindEndpointReplayAsync(
            organizationId, normalizedKey, requestHash, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var endpoint = WebhookEndpoint.Register(
            Guid.NewGuid(), organizationId, command.Url, command.Secret, command.EventTypes, clock.Now);
        await EnsureSafeDeliveryTargetAsync(endpoint.Url, cancellationToken);
        await using var transaction = await BeginOwnedTransactionAsync(cancellationToken);
        var summary = ToSummary(endpoint, true);
        var location = $"/api/webhooks/endpoints/{endpoint.Id}";
        var responseBody = JsonSerializer.Serialize(summary, JsonOptions);
        dbContext.Endpoints.Add(endpoint);
        dbContext.IdempotencyRecords.Add(WebhookIdempotencyRecord.Create(
            Guid.NewGuid(), organizationId, RegisterEndpointOperation, normalizedKey, requestHash,
            201, responseBody, location, clock.Now));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await CommitOwnedAsync(transaction, cancellationToken);
            return new WebhookEndpointMutationResult(
                summary, 201, location, false, responseBody);
        }
        catch (DbUpdateException exception) when (IsIdempotencyConflict(exception))
        {
            await RollbackOwnedAsync(transaction, cancellationToken);
            dbContext.ChangeTracker.Clear();
            return await FindEndpointReplayAsync(
                       organizationId, normalizedKey, requestHash, cancellationToken)
                   ?? throw new InvalidOperationException(
                       "The concurrent webhook idempotency response could not be reloaded.");
        }
    }

    public async Task<Guid> EnqueueAsync(
        Guid organizationId,
        string eventType,
        string aggregateType,
        Guid aggregateId,
        object data,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateType);
        ArgumentOutOfRangeException.ThrowIfEqual(aggregateId, Guid.Empty);
        var eventId = Guid.NewGuid();
        var occurredAt = clock.Now.ToUniversalTime();
        var rawBody = JsonSerializer.SerializeToUtf8Bytes(
            new OutboundWebhookEvent(eventId, eventType, aggregateType, aggregateId, occurredAt, data),
            JsonOptions);
        var item = WebhookOutboxEvent.Create(
            eventId, organizationId, eventType, aggregateType, aggregateId, rawBody, occurredAt);
        dbContext.OutboxEvents.Add(item);
        await dbContext.SaveChangesAsync(cancellationToken);

        return eventId;
    }

    public async Task<IReadOnlyList<WebhookEndpointSummary>> ListEndpointsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        (await dbContext.Endpoints.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken))
        .Select(endpoint => ToSummary(endpoint, false))
        .ToList();

    public async Task<IReadOnlyList<WebhookEventSummary>> ListEventsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var events = await dbContext.OutboxEvents.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId)
            .OrderByDescending(item => item.OccurredAt)
            .ToListAsync(cancellationToken);
        var counts = await dbContext.DeliveryAttempts.AsNoTracking()
            .Where(item => events.Select(eventItem => eventItem.Id).Contains(item.OutboxEventId))
            .GroupBy(item => item.OutboxEventId)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Id, item => item.Count, cancellationToken);
        return events.Select(item => new WebhookEventSummary(
            item.Id, item.OrganizationId, item.EventType, item.AggregateType, item.AggregateId,
            item.OccurredAt, item.DispatchedAt, counts.GetValueOrDefault(item.Id), item.RawBody)).ToList();
    }

    public async Task<IReadOnlyList<WebhookDeliverySummary>?> ListDeliveriesAsync(
        Guid organizationId,
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        if (!await dbContext.OutboxEvents.AsNoTracking()
                .AnyAsync(item => item.OrganizationId == organizationId && item.Id == eventId, cancellationToken))
        {
            return null;
        }

        return (await dbContext.DeliveryAttempts.AsNoTracking()
                .Where(item => item.OutboxEventId == eventId)
                .OrderBy(item => item.AttemptedAt)
                .ToListAsync(cancellationToken))
            .Select(ToSummary)
            .ToList();
    }

    public async Task<InboundWebhookResult> IngestAsync(
        Guid organizationId,
        Guid endpointId,
        byte[] rawBody,
        string signature,
        CancellationToken cancellationToken = default)
    {
        var endpoint = await dbContext.Endpoints.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.Id == endpointId && item.Active)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Webhook endpoint was not found.");
        if (!WebhookSignature.Verify(signature, endpoint.Secret, rawBody, clock.Now, out _))
        {
            throw new WebhookSignatureException("The webhook signature is invalid or stale.");
        }

        var envelope = JsonSerializer.Deserialize<InboundEnvelope>(rawBody, JsonOptions)
            ?? throw new WebhookSignatureException("The webhook payload is invalid.");
        if (envelope.Id == Guid.Empty || string.IsNullOrWhiteSpace(envelope.Type) ||
            envelope.AggregateId == Guid.Empty)
        {
            throw new WebhookSignatureException("The webhook payload is missing required fields.");
        }

        await using var transaction = await BeginOwnedTransactionAsync(cancellationToken);
        var duplicate = await dbContext.InboxEntries.AsNoTracking()
            .AnyAsync(item => item.OrganizationId == organizationId && item.EventId == envelope.Id, cancellationToken);
        if (duplicate)
        {
            await RollbackOwnedAsync(transaction, cancellationToken);
            return new InboundWebhookResult(envelope.Id, true, false, envelope.Type, envelope.Sequence);
        }

        dbContext.InboxEntries.Add(WebhookInboxEntry.Create(
            Guid.NewGuid(), organizationId, envelope.Id, envelope.Type, rawBody,
            envelope.Sequence, clock.Now));
        var now = clock.Now.ToUniversalTime();
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO webhooks.projections
                (id, organization_id, aggregate_id, aggregate_type, sequence, event_type, raw_body, updated_at)
            VALUES
                ({Guid.NewGuid()}, {organizationId}, {envelope.AggregateId}, {envelope.AggregateType},
                 {envelope.Sequence}, {envelope.Type}, {rawBody}, {now})
            ON CONFLICT (organization_id, aggregate_type, aggregate_id)
            DO UPDATE SET
                sequence = EXCLUDED.sequence,
                event_type = EXCLUDED.event_type,
                raw_body = EXCLUDED.raw_body,
                updated_at = EXCLUDED.updated_at
            WHERE webhooks.projections.sequence <= EXCLUDED.sequence;
            """, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await CommitOwnedAsync(transaction, cancellationToken);
            return new InboundWebhookResult(envelope.Id, false, true, envelope.Type, envelope.Sequence);
        }
        catch (DbUpdateException exception) when (IsInboxConflict(exception))
        {
            await RollbackOwnedAsync(transaction, cancellationToken);
            dbContext.ChangeTracker.Clear();
            return new InboundWebhookResult(envelope.Id, true, false, envelope.Type, envelope.Sequence);
        }
    }

    public async Task<WebhookDispatchResult> DispatchAsync(
        Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        await DispatchGate.WaitAsync(cancellationToken);
        try
        {
            var now = clock.Now.ToUniversalTime();
            var query = dbContext.OutboxEvents.AsNoTracking()
                .Where(item => item.DispatchedAt == null && item.AvailableAt <= now);
            if (organizationId is not null)
            {
                query = query.Where(item => item.OrganizationId == organizationId.Value);
            }

            var items = await query.OrderBy(item => item.OccurredAt).Take(100).ToListAsync(cancellationToken);
            var attempts = new List<WebhookDeliverySummary>();
            var delivered = 0;
            var failed = 0;
            var skipped = 0;
            foreach (var item in items)
            {
                // The process-local gate avoids duplicate work inside one host. The lease
                // is the database-level guard for multiple API/worker processes.
                var leaseId = Guid.NewGuid();
                var leaseUntil = now.AddMinutes(5);
                var claimed = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE webhooks.outbox_events
                    SET dispatch_lease_id = {leaseId}, dispatch_lease_until = {leaseUntil}
                    WHERE id = {item.Id}
                      AND dispatched_at IS NULL
                      AND available_at <= {now}
                      AND (dispatch_lease_until IS NULL OR dispatch_lease_until <= {now});
                    """, cancellationToken);
                if (claimed == 0)
                {
                    continue;
                }

                var endpoints = await dbContext.Endpoints.AsNoTracking()
                    .Where(endpoint => endpoint.OrganizationId == item.OrganizationId && endpoint.Active)
                    .ToListAsync(cancellationToken);
                var subscribed = endpoints.Where(endpoint => endpoint.SubscribesTo(item.EventType)).ToList();
                var skippedItem = subscribed.Count == 0;
                var allSucceeded = true;
                var retryAttemptNumber = 1;
                var itemAttempts = new List<WebhookDeliveryAttempt>();
                foreach (var endpoint in subscribed)
                {
                    var previousAttempt = await dbContext.DeliveryAttempts.AsNoTracking()
                        .Where(attempt => attempt.OutboxEventId == item.Id && attempt.EndpointId == endpoint.Id)
                        .OrderByDescending(attempt => attempt.AttemptNumber)
                        .FirstOrDefaultAsync(cancellationToken);
                    if (previousAttempt is not null && previousAttempt.Error is null &&
                        previousAttempt.StatusCode is >= 200 and < 300)
                    {
                        continue;
                    }

                    var attemptNumber = previousAttempt?.AttemptNumber + 1 ?? 1;
                    var started = Stopwatch.GetTimestamp();
                    int? statusCode = null;
                    string? responseBody = null;
                    string? error = null;
                    try
                    {
                        await EnsureSafeDeliveryTargetAsync(endpoint.Url, cancellationToken);
                        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
                        {
                            Content = new ByteArrayContent(item.RawBody)
                        };
                        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                        request.Headers.TryAddWithoutValidation("X-Signature",
                            WebhookSignature.CreateHeader(Encoding.UTF8.GetBytes(endpoint.Secret), now, item.RawBody));
                        request.Headers.TryAddWithoutValidation("X-Event-Id", item.Id.ToString("D"));
                        using var response = await httpClientFactory.CreateClient("webhooks").SendAsync(request, cancellationToken);
                        statusCode = (int)response.StatusCode;
                        responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                        if (!response.IsSuccessStatusCode)
                        {
                            allSucceeded = false;
                            error = $"Webhook endpoint returned HTTP {statusCode}.";
                        }
                    }
                    catch (Exception exception) when (
                        exception is HttpRequestException or TaskCanceledException or
                        ArgumentException or System.Net.Sockets.SocketException)
                    {
                        allSucceeded = false;
                        error = exception.Message;
                    }

                    var duration = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    var delivery = WebhookDeliveryAttempt.Create(
                        Guid.NewGuid(), item.Id, endpoint.Id, now, statusCode, duration,
                        attemptNumber, responseBody, error);
                    itemAttempts.Add(delivery);
                    if (error is not null)
                    {
                        retryAttemptNumber = Math.Max(retryAttemptNumber, attemptNumber);
                    }
                }

                var retryAt = allSucceeded
                    ? (DateTimeOffset?)null
                    : now.AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(8, retryAttemptNumber))));
                if (!await PersistDispatchOutcomeAsync(
                        item.Id,
                        leaseId,
                        now,
                        retryAt,
                        itemAttempts,
                        cancellationToken))
                {
                    continue;
                }

                var persistedAttempts = itemAttempts.Select(ToSummary).ToList();
                attempts.AddRange(persistedAttempts);
                delivered += persistedAttempts.Count(attempt => attempt.Error is null);
                failed += persistedAttempts.Count(attempt => attempt.Error is not null);
                if (skippedItem)
                {
                    skipped++;
                }
            }

            return new WebhookDispatchResult(items.Count, delivered, failed, skipped, attempts);
        }
        finally
        {
            DispatchGate.Release();
        }
    }

    private async Task<bool> PersistDispatchOutcomeAsync(
        Guid outboxEventId,
        Guid leaseId,
        DateTimeOffset dispatchedAt,
        DateTimeOffset? retryAt,
        IReadOnlyList<WebhookDeliveryAttempt> deliveryAttempts,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var updated = retryAt is null
            ? await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE webhooks.outbox_events
                SET dispatched_at = {dispatchedAt},
                    dispatch_lease_id = NULL,
                    dispatch_lease_until = NULL
                WHERE id = {outboxEventId} AND dispatch_lease_id = {leaseId};
                """, cancellationToken)
            : await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE webhooks.outbox_events
                SET available_at = {retryAt.Value},
                    dispatch_lease_id = NULL,
                    dispatch_lease_until = NULL
                WHERE id = {outboxEventId} AND dispatch_lease_id = {leaseId};
                """, cancellationToken);
        if (updated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        dbContext.DeliveryAttempts.AddRange(deliveryAttempts);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<IDbContextTransaction?> BeginOwnedTransactionAsync(CancellationToken cancellationToken) =>
        dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private static Task CommitOwnedAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken) =>
        transaction is null ? Task.CompletedTask : transaction.CommitAsync(cancellationToken);

    private static Task RollbackOwnedAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken) =>
        transaction is null ? Task.CompletedTask : transaction.RollbackAsync(cancellationToken);

    private static bool IsInboxConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres &&
        postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
        postgres.ConstraintName == "ux_webhook_inbox_organization_event";

    private async Task EnsureSafeDeliveryTargetAsync(
        string url,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("Webhook URL must be an absolute HTTP(S) URL without credentials.", nameof(url));
        }

        await destinationResolver.ResolveAllowedAsync(
            uri.DnsSafeHost,
            environment.IsDevelopment() || environment.IsEnvironment("Testing"),
            cancellationToken);
    }

    private async Task<WebhookEndpointMutationResult?> FindEndpointReplayAsync(
        Guid organizationId,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.IdempotencyRecords.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId)
            .Where(item => item.Operation == RegisterEndpointOperation)
            .Where(item => item.IdempotencyKey == idempotencyKey)
            .SingleOrDefaultAsync(cancellationToken);
        if (record is null)
        {
            return null;
        }

        if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new WebhookIdempotencyConflictException();
        }

        var value = JsonSerializer.Deserialize<WebhookEndpointSummary>(record.ResponseBody, JsonOptions)
            ?? throw new InvalidOperationException("Stored webhook idempotency response could not be deserialized.");
        return new WebhookEndpointMutationResult(
            value,
            record.ResponseStatusCode,
            record.ResponseLocation,
            true,
            record.ResponseBody);
    }

    private static bool IsIdempotencyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres &&
        postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
        postgres.ConstraintName == IdempotencyConstraintName;

    private static string NormalizeIdempotencyKey(string idempotencyKey)
    {
        var normalized = idempotencyKey.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 255)
        {
            throw new ArgumentException(
                "Idempotency-Key is required and must be at most 255 characters.",
                nameof(idempotencyKey));
        }

        return normalized;
    }

    private static string HashRegistrationRequest(RegisterWebhookEndpointCommand command)
    {
        var requestBody = JsonSerializer.SerializeToUtf8Bytes(command, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(requestBody));
    }

    private static WebhookEndpointSummary ToSummary(WebhookEndpoint endpoint, bool includeSecret) =>
        new(endpoint.Id, endpoint.OrganizationId, endpoint.Url, endpoint.EventTypes, endpoint.Active,
            endpoint.CreatedAt, includeSecret ? endpoint.Secret : null);

    private static WebhookDeliverySummary ToSummary(WebhookDeliveryAttempt attempt) =>
        new(attempt.Id, attempt.OutboxEventId, attempt.EndpointId, attempt.AttemptedAt,
            attempt.StatusCode, attempt.DurationMilliseconds, attempt.AttemptNumber,
            Math.Max(0, attempt.AttemptNumber - 1),
            attempt.ResponseBody, attempt.Error);

    private sealed record InboundEnvelope(
        Guid Id,
        string Type,
        string AggregateType,
        Guid AggregateId,
        int Sequence,
        DateTimeOffset OccurredAt,
        JsonElement Data);
}
