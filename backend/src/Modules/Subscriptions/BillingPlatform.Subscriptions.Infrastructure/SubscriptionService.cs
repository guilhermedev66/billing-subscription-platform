using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BillingPlatform.Customers.Application;
using BillingPlatform.SimulationClock.Application;
using BillingPlatform.Subscriptions.Application;
using BillingPlatform.Subscriptions.Domain;
using BillingPlatform.Subscriptions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BillingPlatform.Subscriptions.Infrastructure;

internal sealed class SubscriptionService(
    SubscriptionsDbContext dbContext,
    IVirtualClock clock,
    ICustomerService customerService,
    ISubscriptionPriceReader priceReader,
    IProrationCalculator prorationCalculator,
    ISubscriptionRevenueEventWriter revenueEventWriter) : ISubscriptionService, ISubscriptionPaymentStateService
{
    private const string IdempotencyConstraintName =
        "ux_idempotency_records_organization_id_operation_key";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<SubscriptionMutationResult<SubscriptionSummary>?> CreateAsync(
        Guid organizationId,
        CreateSubscriptionCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeIdempotencyKey(idempotencyKey);
        var requestHash = HashRequest(
            $"create|{command.CustomerId:D}|{command.PriceId:D}|{FormatSeatCount(command.SeatCount)}");
        var replay = await FindReplayAsync<SubscriptionSummary>(
            organizationId,
            "create",
            normalizedKey,
            requestHash,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var customer = await customerService.GetAsync(organizationId, command.CustomerId, cancellationToken);
        var price = await priceReader.GetAsync(organizationId, command.PriceId, cancellationToken);
        if (customer is null || price is null)
        {
            return null;
        }

        _ = ResolveRecurringAmount(price, command.SeatCount);
        await using var transaction = await BeginOwnedTransactionAsync(cancellationToken);
        try
        {
            var now = clock.Now.ToUniversalTime();
            var periodEnd = string.Equals(price.BillingInterval, "Month", StringComparison.Ordinal)
                ? now.AddMonths(1)
                : now.AddYears(1);
            DateTimeOffset? trialEnd = price.TrialDays is > 0
                ? now.AddDays(price.TrialDays.Value)
                : null;
            var status = trialEnd is null ? SubscriptionStatus.Active : SubscriptionStatus.Trialing;

            var subscription = Subscription.Create(
                Guid.NewGuid(),
                organizationId,
                customer.Id,
                price.Id,
                status,
                now,
                periodEnd,
                trialEnd,
                command.SeatCount,
                null,
                now);
            dbContext.Subscriptions.Add(subscription);

            return await PersistMutationAsync(
                organizationId,
                "create",
                normalizedKey,
                requestHash,
                ToSummary(subscription),
                201,
                $"/api/subscriptions/{subscription.Id}",
                transaction,
                cancellationToken,
                CreateRevenueEvent(null, subscription, price, "created"));
        }
        catch (ArgumentException)
        {
            await RollbackOwnedAsync(transaction, cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> ListAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.OrganizationId == organizationId)
            .OrderBy(subscription => subscription.CurrentPeriodEnd)
            .ThenBy(subscription => subscription.Id)
            .Select(ToSummaryExpression())
            .ToListAsync(cancellationToken);

    public async Task<SubscriptionSummary?> GetAsync(
        Guid organizationId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.OrganizationId == organizationId)
            .Where(subscription => subscription.Id == subscriptionId)
            .Select(ToSummaryExpression())
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<SubscriptionProrationReceipt?> PreviewChangeAsync(
        Guid organizationId,
        Guid subscriptionId,
        SubscriptionChangeCommand command,
        CancellationToken cancellationToken = default)
    {
        var subscription = await GetTrackedAsync(organizationId, subscriptionId, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        return (await BuildReceiptAsync(subscription, command, cancellationToken)).Receipt;
    }

    public async Task<SubscriptionMutationResult<SubscriptionProrationReceipt>?> ApplyChangeAsync(
        Guid organizationId,
        Guid subscriptionId,
        SubscriptionChangeCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeIdempotencyKey(idempotencyKey);
        var requestHash = HashRequest(
            $"apply-change|{subscriptionId:D}|{FormatGuid(command.NewPriceId)}|{FormatSeatCount(command.NewSeatCount)}|{NormalizeCardNumber(command.CardNumber)}");
        var replay = await FindReplayAsync<SubscriptionProrationReceipt>(
            organizationId,
            "apply-change",
            normalizedKey,
            requestHash,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        await using var transaction = await BeginOwnedTransactionAsync(cancellationToken);
        try
        {
            var subscription = await GetTrackedAsync(organizationId, subscriptionId, cancellationToken);
            if (subscription is null)
            {
                await RollbackOwnedAsync(transaction, cancellationToken);
                return null;
            }

            var prepared = await BuildReceiptAsync(subscription, command, cancellationToken);
            var before = ToRevenueState(subscription, prepared.CurrentPrice);
            if (subscription.PriceId != prepared.NewPriceId || subscription.SeatCount != prepared.NewSeatCount)
            {
                subscription.ChangePlan(prepared.NewPriceId, prepared.NewSeatCount);
            }

            // M4 attaches this receipt to invoice/proration line items; M3 intentionally persists no invoice.
            var receipt = prepared.Receipt with { Subscription = ToSummary(subscription) };
            return await PersistMutationAsync(
                organizationId,
                "apply-change",
                normalizedKey,
                requestHash,
                receipt,
                200,
                null,
                transaction,
                cancellationToken,
                CreateRevenueEvent(before, subscription, prepared.NewPrice, "changed"));
        }
        catch (ArgumentException)
        {
            await RollbackOwnedAsync(transaction, cancellationToken);
            throw;
        }
        catch (SubscriptionDomainException)
        {
            await RollbackOwnedAsync(transaction, cancellationToken);
            throw;
        }
    }

    public Task<SubscriptionMutationResult<SubscriptionSummary>?> CancelAsync(
        Guid organizationId,
        Guid subscriptionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(organizationId, subscriptionId, "cancel", idempotencyKey,
            subscription => subscription.Cancel(clock.Now), cancellationToken);

    public Task<SubscriptionMutationResult<SubscriptionSummary>?> PauseAsync(
        Guid organizationId,
        Guid subscriptionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(organizationId, subscriptionId, "pause", idempotencyKey,
            subscription => subscription.Pause(), cancellationToken);

    public Task<SubscriptionMutationResult<SubscriptionSummary>?> ResumeAsync(
        Guid organizationId,
        Guid subscriptionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(organizationId, subscriptionId, "resume", idempotencyKey,
            subscription => subscription.Resume(), cancellationToken);

    public async Task<SubscriptionSummary?> RenewAsync(
        Guid organizationId,
        Guid subscriptionId,
        DateTimeOffset now,
        DateTimeOffset newPeriodEnd,
        CancellationToken cancellationToken = default)
    {
        var subscription = await GetTrackedAsync(organizationId, subscriptionId, cancellationToken);
        if (subscription is null || subscription.Status != SubscriptionStatus.Active ||
            subscription.CurrentPeriodEnd > now.ToUniversalTime())
        {
            return null;
        }

        var price = await RequirePriceAsync(subscription, cancellationToken);
        var before = ToRevenueState(subscription, price);
        subscription.Renew(newPeriodEnd);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await revenueEventWriter.AppendAsync(
                CreateRevenueEvent(before, subscription, price, "renewed"), cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            throw new SubscriptionConcurrencyException();
        }

        return ToSummary(subscription);
    }

    public Task<SubscriptionSummary?> MarkPastDueAsync(
        Guid organizationId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default) =>
        TransitionPaymentStateAsync(
            organizationId,
            subscriptionId,
            subscription => subscription.MarkPastDue(),
            "past_due",
            cancellationToken);

    public Task<SubscriptionSummary?> MarkUnpaidAsync(
        Guid organizationId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default) =>
        TransitionPaymentStateAsync(
            organizationId,
            subscriptionId,
            subscription => subscription.MarkUnpaid(),
            "unpaid",
            cancellationToken);

    public Task<SubscriptionSummary?> RecoverAsync(
        Guid organizationId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default) =>
        TransitionPaymentStateAsync(
            organizationId,
            subscriptionId,
            subscription => subscription.Recover(),
            "recovered",
            cancellationToken);

    private async Task<SubscriptionSummary?> TransitionPaymentStateAsync(
        Guid organizationId,
        Guid subscriptionId,
        Action<Subscription> transition,
        string reason,
        CancellationToken cancellationToken)
    {
        var subscription = await GetTrackedAsync(organizationId, subscriptionId, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        var price = await RequirePriceAsync(subscription, cancellationToken);
        var before = ToRevenueState(subscription, price);
        transition(subscription);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await revenueEventWriter.AppendAsync(
                CreateRevenueEvent(before, subscription, price, reason), cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            throw new SubscriptionConcurrencyException();
        }

        return ToSummary(subscription);
    }

    private async Task<SubscriptionMutationResult<SubscriptionSummary>?> TransitionAsync(
        Guid organizationId,
        Guid subscriptionId,
        string operation,
        string idempotencyKey,
        Action<Subscription> transition,
        CancellationToken cancellationToken)
    {
        var normalizedKey = NormalizeIdempotencyKey(idempotencyKey);
        var requestHash = HashRequest($"{operation}|{subscriptionId:D}");
        var replay = await FindReplayAsync<SubscriptionSummary>(
            organizationId, operation, normalizedKey, requestHash, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        await using var transaction = await BeginOwnedTransactionAsync(cancellationToken);
        try
        {
            var subscription = await GetTrackedAsync(organizationId, subscriptionId, cancellationToken);
            if (subscription is null)
            {
                await RollbackOwnedAsync(transaction, cancellationToken);
                return null;
            }

            var price = await RequirePriceAsync(subscription, cancellationToken);
            var before = ToRevenueState(subscription, price);
            transition(subscription);
            return await PersistMutationAsync(
                organizationId,
                operation,
                normalizedKey,
                requestHash,
                ToSummary(subscription),
                200,
                null,
                transaction,
                cancellationToken,
                CreateRevenueEvent(before, subscription, price, operation));
        }
        catch (SubscriptionDomainException)
        {
            await RollbackOwnedAsync(transaction, cancellationToken);
            throw;
        }
    }

    private async Task<PreparedChange> BuildReceiptAsync(
        Subscription subscription,
        SubscriptionChangeCommand command,
        CancellationToken cancellationToken)
    {
        if (!command.HasChange)
        {
            throw new ArgumentException(
                "At least one of newPriceId or newSeatCount is required.", nameof(command));
        }

        var currentPrice = await priceReader.GetAsync(
            subscription.OrganizationId, subscription.PriceId, cancellationToken)
            ?? throw new ArgumentException("The current subscription price no longer exists.", nameof(command));
        var newPrice = command.NewPriceId is null
            ? currentPrice
            : await priceReader.GetAsync(
                subscription.OrganizationId, command.NewPriceId.Value, cancellationToken)
                ?? throw new ArgumentException(
                    "The requested price was not found in the organization.", nameof(command));

        if (!string.Equals(currentPrice.Currency, newPrice.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Plan changes must use the same currency as the current subscription.", nameof(command));
        }

        var targetSeatCount = ResolveTargetSeatCount(newPrice, command.NewSeatCount, subscription.SeatCount);
        var currentAmount = ResolveRecurringAmount(currentPrice, subscription.SeatCount);
        var newAmount = ResolveRecurringAmount(newPrice, targetSeatCount);
        var input = new ProrationInput(
            new ProrationPlan(currentPrice.Id, currentPrice.Currency, currentAmount),
            new ProrationPlan(newPrice.Id, newPrice.Currency, newAmount),
            subscription.CurrentPeriodStart,
            subscription.CurrentPeriodEnd,
            clock.Now);
        var proration = subscription.Status == SubscriptionStatus.Trialing
            ? prorationCalculator.CalculateNoCharge(input)
            : prorationCalculator.Calculate(input);

        return new PreparedChange(
            new SubscriptionProrationReceipt(ToSummary(subscription), proration),
            newPrice.Id,
            targetSeatCount,
            currentPrice,
            newPrice);
    }

    private async Task<SubscriptionMutationResult<T>?> FindReplayAsync<T>(
        Guid organizationId,
        string operation,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.IdempotencyRecords
            .AsNoTracking()
            .Where(record => record.OrganizationId == organizationId)
            .Where(record => record.Operation == operation)
            .Where(record => record.IdempotencyKey == idempotencyKey)
            .SingleOrDefaultAsync(cancellationToken);
        return record is null ? null : ToReplay<T>(record, requestHash);
    }

    private async Task<SubscriptionMutationResult<T>> PersistMutationAsync<T>(
        Guid organizationId,
        string operation,
        string idempotencyKey,
        string requestHash,
        T value,
        int statusCode,
        string? location,
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken,
        SubscriptionRevenueEvent sourceEvent)
    {
        var responseBody = JsonSerializer.Serialize(value, JsonOptions);
        dbContext.IdempotencyRecords.Add(
            SubscriptionIdempotencyRecord.Create(
                Guid.NewGuid(), organizationId, operation, idempotencyKey, requestHash,
                statusCode, responseBody, location, clock.Now));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await revenueEventWriter.AppendAsync(sourceEvent, cancellationToken);
            await CommitOwnedAsync(transaction, cancellationToken);
            return new SubscriptionMutationResult<T>(value, statusCode, location, false, responseBody);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ResolveFailedMutationAsync<T>(
                organizationId, operation, idempotencyKey, requestHash, transaction, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsIdempotencyConflict(exception))
        {
            return await ResolveFailedMutationAsync<T>(
                organizationId, operation, idempotencyKey, requestHash, transaction, cancellationToken);
        }
    }

    private async Task<SubscriptionMutationResult<T>> ResolveFailedMutationAsync<T>(
        Guid organizationId,
        string operation,
        string idempotencyKey,
        string requestHash,
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await RollbackOwnedAsync(transaction, cancellationToken);
        dbContext.ChangeTracker.Clear();
        var record = await dbContext.IdempotencyRecords
            .AsNoTracking()
            .Where(record => record.OrganizationId == organizationId)
            .Where(record => record.Operation == operation)
            .Where(record => record.IdempotencyKey == idempotencyKey)
            .SingleOrDefaultAsync(cancellationToken);
        if (record is not null)
        {
            return ToReplay<T>(record, requestHash);
        }

        throw new SubscriptionConcurrencyException();
    }

    private static bool IsIdempotencyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException &&
        postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
        postgresException.ConstraintName == IdempotencyConstraintName;

    private static SubscriptionMutationResult<T> ToReplay<T>(
        SubscriptionIdempotencyRecord record, string requestHash)
    {
        if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new IdempotencyConflictException();
        }

        var value = JsonSerializer.Deserialize<T>(record.ResponseBody, JsonOptions)
            ?? throw new InvalidOperationException("Stored idempotency response could not be deserialized.");
        return new SubscriptionMutationResult<T>(
            value, record.ResponseStatusCode, record.ResponseLocation, true, record.ResponseBody);
    }

    private static string NormalizeIdempotencyKey(string idempotencyKey)
    {
        var normalized = idempotencyKey.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 255)
        {
            throw new ArgumentException(
                "Idempotency-Key is required and must be at most 255 characters.", nameof(idempotencyKey));
        }

        return normalized;
    }

    private static string NormalizeCardNumber(string? cardNumber) =>
        string.IsNullOrWhiteSpace(cardNumber)
            ? "<null>"
            : new string(cardNumber.Where(character => !char.IsWhiteSpace(character)).ToArray());

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

    private static string HashRequest(string normalizedRequest) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRequest)));

    private static string FormatGuid(Guid? value) => value?.ToString("D") ?? "<null>";

    private static string FormatSeatCount(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "<null>";

    private static int? ResolveTargetSeatCount(
        SubscriptionPrice price,
        int? requestedSeatCount,
        int? currentSeatCount) =>
        price.PricingModel switch
        {
            "Flat" or "Metered" => null,
            "PerSeat" or "Tiered" => requestedSeatCount ?? RequireSeatCount(currentSeatCount),
            _ => throw new ArgumentOutOfRangeException(nameof(price.PricingModel))
        };

    private static long ResolveRecurringAmount(SubscriptionPrice price, int? seatCount) =>
        price.PricingModel switch
        {
            "Flat" => ResolveFlatAmount(price, seatCount),
            "PerSeat" => checked(price.PerSeatUnitAmountCents.GetValueOrDefault() * RequireSeatCount(seatCount)),
            "Tiered" => CalculateTieredAmount(price, RequireSeatCount(seatCount)),
            "Metered" => ResolveMeteredAmount(price, seatCount),
            _ => throw new ArgumentOutOfRangeException(nameof(price.PricingModel))
        };

    private static long ResolveFlatAmount(SubscriptionPrice price, int? seatCount)
    {
        EnsureNoSeatCount(price, seatCount);
        return price.FlatUnitAmountCents
            ?? throw new ArgumentException("Flat price is missing its unit amount.");
    }

    private static long ResolveMeteredAmount(SubscriptionPrice price, int? seatCount)
    {
        EnsureNoSeatCount(price, seatCount);
        // M4 will rate usage and attach metered line items; M3 has no usage ledger or invoice.
        return 0;
    }

    private static long CalculateTieredAmount(SubscriptionPrice price, int seatCount)
    {
        long total = 0;
        var coveredThrough = 0;
        foreach (var tier in price.Tiers.OrderBy(tier => tier.StartingUnit))
        {
            if (tier.StartingUnit > seatCount)
            {
                break;
            }

            var endingUnit = Math.Min(tier.EndingUnit ?? seatCount, seatCount);
            var units = endingUnit - tier.StartingUnit + 1;
            if (units > 0)
            {
                total = checked(total + checked(units * tier.UnitAmountCents));
                coveredThrough = endingUnit;
            }
        }

        if (coveredThrough < seatCount)
        {
            throw new ArgumentException("The tiered price does not cover the requested seat count.");
        }

        return total;
    }

    private static int RequireSeatCount(int? seatCount) =>
        seatCount is > 0
            ? seatCount.Value
            : throw new ArgumentOutOfRangeException(
                nameof(seatCount), "This pricing model requires a positive seat count.");

    private static void EnsureNoSeatCount(SubscriptionPrice price, int? seatCount)
    {
        if (seatCount is not null)
        {
            throw new ArgumentException(
                $"Seat count is not meaningful for {price.PricingModel} prices.", nameof(seatCount));
        }
    }

    private Task<Subscription?> GetTrackedAsync(
        Guid organizationId,
        Guid subscriptionId,
        CancellationToken cancellationToken) =>
        dbContext.Subscriptions
            .Where(subscription => subscription.OrganizationId == organizationId)
            .Where(subscription => subscription.Id == subscriptionId)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<SubscriptionPrice> RequirePriceAsync(
        Subscription subscription,
        CancellationToken cancellationToken) =>
        await priceReader.GetAsync(
            subscription.OrganizationId, subscription.PriceId, cancellationToken)
        ?? throw new InvalidOperationException("The subscription price is missing.");

    private SubscriptionRevenueEvent CreateRevenueEvent(
        SubscriptionRevenueState? before,
        Subscription subscription,
        SubscriptionPrice afterPrice,
        string reason) =>
        new(
            Guid.NewGuid(), subscription.OrganizationId, subscription.Id, reason,
            clock.Now.ToUniversalTime(), before, ToRevenueState(subscription, afterPrice));

    private static SubscriptionRevenueState ToRevenueState(
        Subscription subscription,
        SubscriptionPrice price) =>
        new(
            subscription.Status.ToString(),
            subscription.PriceId,
            subscription.SeatCount,
            subscription.Version,
            new RevenuePriceTerms(
                price.Id,
                price.Version,
                price.Currency,
                price.PricingModel,
                price.BillingInterval,
                price.FlatUnitAmountCents,
                price.PerSeatUnitAmountCents,
                price.Tiers));

    private static SubscriptionSummary ToSummary(Subscription subscription) =>
        new(
            subscription.Id, subscription.OrganizationId, subscription.CustomerId,
            subscription.PriceId, subscription.Status, subscription.CurrentPeriodStart,
            subscription.CurrentPeriodEnd, subscription.TrialEnd, subscription.SeatCount,
            subscription.CanceledAt, subscription.CreatedAt, subscription.Version);

    private static System.Linq.Expressions.Expression<Func<Subscription, SubscriptionSummary>> ToSummaryExpression() =>
        subscription => new SubscriptionSummary(
            subscription.Id, subscription.OrganizationId, subscription.CustomerId,
            subscription.PriceId, subscription.Status, subscription.CurrentPeriodStart,
            subscription.CurrentPeriodEnd, subscription.TrialEnd, subscription.SeatCount,
            subscription.CanceledAt, subscription.CreatedAt, subscription.Version);

    private sealed record PreparedChange(
        SubscriptionProrationReceipt Receipt,
        Guid NewPriceId,
        int? NewSeatCount,
        SubscriptionPrice CurrentPrice,
        SubscriptionPrice NewPrice);
}
