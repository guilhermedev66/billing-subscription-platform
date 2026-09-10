using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BillingPlatform.Billing.Application;
using BillingPlatform.Billing.Domain;
using BillingPlatform.Payments.Application;
using BillingPlatform.Payments.Domain;
using BillingPlatform.Payments.Infrastructure.Persistence;
using BillingPlatform.SimulationClock.Application;
using BillingPlatform.Subscriptions.Application;
using BillingPlatform.Subscriptions.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BillingPlatform.Payments.Infrastructure;

internal sealed class PaymentService(
    PaymentsDbContext dbContext,
    IInvoiceService invoiceService,
    ISubscriptionPaymentStateService subscriptionService,
    IVirtualClock clock) : IPaymentService
{
    private const string IdempotencyConstraintName =
        "ux_payment_attempts_organization_id_operation_key";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyDictionary<string, (PaymentOutcome Outcome, string Last4)> Cards =
        new Dictionary<string, (PaymentOutcome, string)>(StringComparer.Ordinal)
        {
            ["4242424242424242"] = (PaymentOutcome.Succeeded, "4242"),
            ["4000000000000002"] = (PaymentOutcome.Declined, "0002"),
            ["4000000000000004"] = (PaymentOutcome.InsufficientFunds, "0004"),
            ["4000000000000005"] = (PaymentOutcome.Expired, "0005"),
            ["4000000000003022"] = (PaymentOutcome.Requires3DS, "3022"),
            ["4000000000000007"] = (PaymentOutcome.ProcessingError, "0007")
        };

    public async Task<IReadOnlyList<PaymentAttemptSummary>?> ListAttemptsAsync(
        Guid organizationId,
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        if (await invoiceService.GetAsync(organizationId, invoiceId, cancellationToken) is null)
        {
            return null;
        }

        return (await dbContext.PaymentAttempts
                .AsNoTracking()
                .Where(attempt => attempt.OrganizationId == organizationId)
                .Where(attempt => attempt.InvoiceId == invoiceId)
                .OrderBy(attempt => attempt.AttemptNumber)
                .ThenBy(attempt => attempt.AttemptedAt)
                .ThenBy(attempt => attempt.Id)
                .ToListAsync(cancellationToken))
            .Select(ToSummary)
            .ToList();
    }

    public Task<PaymentResult?> AttemptAsync(
        Guid organizationId,
        Guid invoiceId,
        string cardNumber,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        AttemptInternalAsync(
            organizationId,
            invoiceId,
            cardNumber,
            "charge",
            NormalizeKey(idempotencyKey),
            cancellationToken);

    public async Task<DunningSweepResponse> SweepDunningAsync(
        Guid organizationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeKey(idempotencyKey);
        var requestHash = HashRequest($"dunning-sweep|{organizationId:D}");
        var storedSweep = await dbContext.SweepIdempotencyRecords
            .AsNoTracking()
            .Where(record => record.OrganizationId == organizationId)
            .Where(record => record.IdempotencyKey == normalizedKey)
            .SingleOrDefaultAsync(cancellationToken);
        if (storedSweep is not null)
        {
            return ToSweepReplay(storedSweep, requestHash);
        }

        await using var ownedTransaction = await BeginOwnedTransactionAsync(cancellationToken);
        var now = clock.Now.ToUniversalTime();
        var dueInvoices = (await invoiceService.ListAsync(organizationId, cancellationToken))
            .Where(invoice => invoice.Status.ToString() == "Open")
            .Where(invoice => invoice.SubscriptionId is not null)
            .Where(invoice => invoice.NextRetryAt is not null && invoice.NextRetryAt <= now)
            .ToList();
        var results = new List<PaymentResult>();
        foreach (var invoice in dueInvoices)
        {
            var lastAttempt = await dbContext.PaymentAttempts
                .AsNoTracking()
                .Where(attempt => attempt.OrganizationId == organizationId)
                .Where(attempt => attempt.InvoiceId == invoice.Id)
                .OrderByDescending(attempt => attempt.AttemptedAt)
                .ThenByDescending(attempt => attempt.Id)
                .FirstOrDefaultAsync(cancellationToken);
            // M0 §2.6 sends only business declines through automated dunning. Expired cards
            // require a new manual payment method; 3DS and processing errors are not declines.
            if (lastAttempt?.Outcome is not (PaymentOutcome.Declined or PaymentOutcome.InsufficientFunds) ||
                !TryResolveCard(lastAttempt.CardNumberLast4, out var cardNumber))
            {
                continue;
            }

            var dunningKey = $"dunning-{invoice.Id:D}-{invoice.DunningAttemptCount}";
            var result = await AttemptInternalAsync(
                organizationId,
                invoice.Id,
                cardNumber,
                "dunning",
                dunningKey,
                cancellationToken);
            if (result is not null)
            {
                results.Add(result);
            }
        }

        var sweepResult = new DunningSweepResult(
            dueInvoices.Count,
            results.Count,
            results.Count(result => result.Attempt.Outcome == PaymentOutcome.Succeeded),
            results.Count(result => result.Attempt.Outcome != PaymentOutcome.Succeeded),
            results.Count(result => result.Invoice.Status.ToString() == "Uncollectible"),
            results);
        var responseBody = JsonSerializer.Serialize(sweepResult, JsonOptions);
        dbContext.SweepIdempotencyRecords.Add(PaymentSweepIdempotencyRecord.Create(
            Guid.NewGuid(), organizationId, normalizedKey, requestHash, responseBody, clock.Now));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await CommitOwnedAsync(ownedTransaction, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsSweepIdempotencyConflict(exception))
        {
            await RollbackOwnedAsync(ownedTransaction, cancellationToken);
            dbContext.ChangeTracker.Clear();
            var replay = await dbContext.SweepIdempotencyRecords
                .AsNoTracking()
                .Where(record => record.OrganizationId == organizationId)
                .Where(record => record.IdempotencyKey == normalizedKey)
                .SingleAsync(cancellationToken);
            return ToSweepReplay(replay, requestHash);
        }

        return new DunningSweepResponse(sweepResult, false, responseBody);
    }

    private async Task<PaymentResult?> AttemptInternalAsync(
        Guid organizationId,
        Guid invoiceId,
        string cardNumber,
        string operation,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var normalizedCard = NormalizeCard(cardNumber);
        if (!Cards.TryGetValue(normalizedCard, out var card))
        {
            throw new ArgumentException("The card number is not one of the supported simulator cards.", nameof(cardNumber));
        }

        var requestHash = HashRequest($"{operation}|{invoiceId:D}|{normalizedCard}");
        var replay = await FindReplayAsync(organizationId, operation, idempotencyKey, requestHash, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        await using var transaction = await BeginOwnedTransactionAsync(cancellationToken);
        try
        {
            var existing = await dbContext.PaymentAttempts
                .SingleOrDefaultAsync(attempt =>
                    attempt.OrganizationId == organizationId &&
                    attempt.Operation == operation &&
                    attempt.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            if (existing is not null)
            {
                return await CompleteReplayAsync(existing, requestHash, transaction, cancellationToken);
            }

            var invoice = await invoiceService.GetAsync(organizationId, invoiceId, cancellationToken);
            if (invoice is null)
            {
                await RollbackOwnedAsync(transaction, cancellationToken);
                return null;
            }

            if (invoice.Status != InvoiceStatus.Open)
            {
                await RollbackOwnedAsync(transaction, cancellationToken);
                throw new PaymentStateConflictException("Only open invoices can be charged.");
            }

            if (card.Outcome == PaymentOutcome.ProcessingError)
            {
                throw new PaymentProcessingException();
            }

            var previousAttempt = await dbContext.PaymentAttempts
                .AsNoTracking()
                .Where(attempt => attempt.OrganizationId == organizationId)
                .Where(attempt => attempt.InvoiceId == invoiceId)
                .OrderByDescending(attempt => attempt.AttemptedAt)
                .ThenByDescending(attempt => attempt.Id)
                .FirstOrDefaultAsync(cancellationToken);
            var outcome = card.Outcome == PaymentOutcome.Requires3DS &&
                          previousAttempt?.Outcome == PaymentOutcome.Requires3DS &&
                          string.Equals(
                              previousAttempt.CardNumberLast4,
                              card.Last4,
                              StringComparison.Ordinal)
                ? PaymentOutcome.Succeeded
                : card.Outcome;
            var attemptNumber = previousAttempt?.AttemptNumber + 1 ?? invoice.DunningAttemptCount + 1;
            var attempt = PaymentAttempt.Create(
                Guid.NewGuid(),
                organizationId,
                invoiceId,
                card.Last4,
                outcome,
                clock.Now,
                operation,
                idempotencyKey,
                requestHash,
                "{\"pending\":true}",
                200,
                attemptNumber);
            dbContext.PaymentAttempts.Add(attempt);
            await dbContext.SaveChangesAsync(cancellationToken);

            InvoiceSummary? updatedInvoice;
            if (outcome == PaymentOutcome.Succeeded)
            {
                updatedInvoice = await invoiceService.MarkPaidAsync(
                    organizationId, invoiceId, clock.Now, cancellationToken);
                await RecoverSubscriptionAsync(organizationId, invoice.SubscriptionId, cancellationToken);
            }
            else if (outcome is PaymentOutcome.Declined or PaymentOutcome.InsufficientFunds)
            {
                updatedInvoice = await invoiceService.RecordPaymentFailureAsync(
                    organizationId, invoiceId, clock.Now, cancellationToken);
                await MarkSubscriptionAfterFailureAsync(
                    organizationId, invoice.SubscriptionId, updatedInvoice, cancellationToken);
            }
            else
            {
                updatedInvoice = invoice;
            }

            if (updatedInvoice is null)
            {
                await RollbackOwnedAsync(transaction, cancellationToken);
                return null;
            }

            var summary = ToSummary(attempt);
            var responseBody = JsonSerializer.Serialize(
                new StoredPaymentResponse(summary, updatedInvoice), JsonOptions);
            attempt.Complete(responseBody);
            await dbContext.SaveChangesAsync(cancellationToken);
            await CommitOwnedAsync(transaction, cancellationToken);
            return new PaymentResult(summary, updatedInvoice, false, responseBody);
        }
        catch (DbUpdateException exception) when (IsIdempotencyConflict(exception))
        {
            return await ResolveUniqueConflictAsync(
                organizationId, operation, idempotencyKey, requestHash, transaction, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await RollbackOwnedAsync(transaction, cancellationToken);
            dbContext.ChangeTracker.Clear();
            throw new PaymentConcurrencyException();
        }
        catch (InvoiceConcurrencyException)
        {
            await RollbackOwnedAsync(transaction, cancellationToken);
            dbContext.ChangeTracker.Clear();
            throw new PaymentConcurrencyException();
        }
        catch (SubscriptionConcurrencyException)
        {
            await RollbackOwnedAsync(transaction, cancellationToken);
            dbContext.ChangeTracker.Clear();
            throw new PaymentConcurrencyException();
        }
        catch (InvoiceDomainException)
        {
            await RollbackOwnedAsync(transaction, cancellationToken);
            dbContext.ChangeTracker.Clear();
            throw new PaymentConcurrencyException();
        }
        catch (SubscriptionDomainException)
        {
            await RollbackOwnedAsync(transaction, cancellationToken);
            dbContext.ChangeTracker.Clear();
            throw new PaymentConcurrencyException();
        }
    }

    private async Task<PaymentResult?> FindReplayAsync(
        Guid organizationId,
        string operation,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var attempt = await dbContext.PaymentAttempts
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId)
            .Where(item => item.Operation == operation)
            .Where(item => item.IdempotencyKey == idempotencyKey)
            .SingleOrDefaultAsync(cancellationToken);
        return attempt is null ? null : ToReplay(attempt, requestHash);
    }

    private async Task<PaymentResult> CompleteReplayAsync(
        PaymentAttempt attempt,
        string requestHash,
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var replay = ToReplay(attempt, requestHash);
        await RollbackOwnedAsync(transaction, cancellationToken);
        return replay;
    }

    private async Task<PaymentResult> ResolveUniqueConflictAsync(
        Guid organizationId,
        string operation,
        string idempotencyKey,
        string requestHash,
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await RollbackOwnedAsync(transaction, cancellationToken);
        dbContext.ChangeTracker.Clear();
        var attempt = await dbContext.PaymentAttempts
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId)
            .Where(item => item.Operation == operation)
            .Where(item => item.IdempotencyKey == idempotencyKey)
            .SingleAsync(cancellationToken);
        return ToReplay(attempt, requestHash);
    }

    private async Task MarkSubscriptionAfterFailureAsync(
        Guid organizationId,
        Guid? subscriptionId,
        InvoiceSummary? invoice,
        CancellationToken cancellationToken)
    {
        if (subscriptionId is null || invoice is null)
        {
            return;
        }

        var subscription = await subscriptionService.GetAsync(organizationId, subscriptionId.Value, cancellationToken);
        if (subscription?.Status.ToString() is "Active" or "Trialing")
        {
            await subscriptionService.MarkPastDueAsync(organizationId, subscriptionId.Value, cancellationToken);
        }

        if (invoice.Status.ToString() == "Uncollectible")
        {
            subscription = await subscriptionService.GetAsync(organizationId, subscriptionId.Value, cancellationToken);
            if (subscription?.Status.ToString() == "PastDue")
            {
                await subscriptionService.MarkUnpaidAsync(organizationId, subscriptionId.Value, cancellationToken);
            }
        }
    }

    private async Task RecoverSubscriptionAsync(
        Guid organizationId,
        Guid? subscriptionId,
        CancellationToken cancellationToken)
    {
        if (subscriptionId is null)
        {
            return;
        }

        var subscription = await subscriptionService.GetAsync(organizationId, subscriptionId.Value, cancellationToken);
        if (subscription?.Status.ToString() is "PastDue" or "Unpaid")
        {
            await subscriptionService.RecoverAsync(organizationId, subscriptionId.Value, cancellationToken);
        }
    }

    private static string NormalizeCard(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new string(value.Where(character => !char.IsWhiteSpace(character)).ToArray());
    }

    private async Task<IDbContextTransaction?> BeginOwnedTransactionAsync(
        CancellationToken cancellationToken) =>
        dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private static Task CommitOwnedAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken) =>
        transaction is null ? Task.CompletedTask : transaction.CommitAsync(cancellationToken);

    private static Task RollbackOwnedAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken) =>
        transaction is null ? Task.CompletedTask : transaction.RollbackAsync(cancellationToken);

    private static string NormalizeKey(string value)
    {
        var normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 255)
        {
            throw new ArgumentException("Idempotency-Key is required and must be at most 255 characters.", nameof(value));
        }

        return normalized;
    }

    private static string HashRequest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static PaymentAttemptSummary ToSummary(PaymentAttempt attempt) =>
        new(
            attempt.Id,
            attempt.OrganizationId,
            attempt.InvoiceId,
            attempt.CardNumberLast4,
            attempt.Outcome,
            attempt.AttemptedAt,
            attempt.AttemptNumber);

    private static PaymentResult ToReplay(PaymentAttempt attempt, string requestHash)
    {
        if (!string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new PaymentIdempotencyConflictException();
        }

        var stored = JsonSerializer.Deserialize<StoredPaymentResponse>(attempt.ResponseBody, JsonOptions)
            ?? throw new InvalidOperationException("Stored payment response could not be deserialized.");
        return new PaymentResult(stored.Attempt, stored.Invoice, true, attempt.ResponseBody);
    }

    private static bool IsIdempotencyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException &&
        postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
        postgresException.ConstraintName == IdempotencyConstraintName;

    private static bool IsSweepIdempotencyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException &&
        postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
        postgresException.ConstraintName == "ux_sweep_idempotency_records_organization_id_key";

    private static DunningSweepResponse ToSweepReplay(
        PaymentSweepIdempotencyRecord record,
        string requestHash)
    {
        if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new PaymentIdempotencyConflictException();
        }

        var value = JsonSerializer.Deserialize<DunningSweepResult>(record.ResponseBody, JsonOptions)
            ?? throw new InvalidOperationException("Stored dunning sweep response could not be deserialized.");
        return new DunningSweepResponse(value, true, record.ResponseBody);
    }

    private static bool TryResolveCard(string last4, out string cardNumber)
    {
        var card = Cards.FirstOrDefault(item => item.Value.Last4 == last4);
        if (card.Key is null)
        {
            cardNumber = string.Empty;
            return false;
        }

        cardNumber = card.Key;
        return true;
    }

    private sealed record StoredPaymentResponse(
        PaymentAttemptSummary Attempt,
        InvoiceSummary Invoice);
}
