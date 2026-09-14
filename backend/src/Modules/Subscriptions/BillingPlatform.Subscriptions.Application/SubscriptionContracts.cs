using BillingPlatform.Subscriptions.Domain;

namespace BillingPlatform.Subscriptions.Application;

public sealed class SubscriptionConcurrencyException()
    : Exception("The subscription was modified by another request.");

public sealed class IdempotencyConflictException()
    : Exception("The Idempotency-Key was already used with a different request.");

public sealed class SubscriptionBillingConflictException(string message) : Exception(message);

public sealed record SubscriptionMutationResult<T>(
    T Value,
    int StatusCode,
    string? Location,
    bool Replayed,
    string ResponseBody);

public sealed record CreateSubscriptionCommand(
    Guid CustomerId,
    Guid PriceId,
    int? SeatCount);

public sealed record SubscriptionChangeCommand(
    Guid? NewPriceId,
    int? NewSeatCount,
    string? CardNumber = null)
{
    public bool HasChange => NewPriceId is not null || NewSeatCount is not null;
}

public sealed record SubscriptionSummary(
    Guid Id,
    Guid OrganizationId,
    Guid CustomerId,
    Guid PriceId,
    SubscriptionStatus Status,
    DateTimeOffset CurrentPeriodStart,
    DateTimeOffset CurrentPeriodEnd,
    DateTimeOffset? TrialEnd,
    int? SeatCount,
    DateTimeOffset? CanceledAt,
    DateTimeOffset CreatedAt,
    int Version);

public sealed record SubscriptionProrationReceipt(
    SubscriptionSummary Subscription,
    ProrationResult Proration);

public interface ISubscriptionChangeBillingOrchestrator
{
    Task<SubscriptionMutationResult<SubscriptionProrationReceipt>?> ApplyAsync(
        Guid organizationId,
        Guid subscriptionId,
        SubscriptionChangeCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public interface ISubscriptionMutationCoordinator
{
    Task<SubscriptionMutationResult<SubscriptionSummary>?> CreateAsync(
        Guid organizationId,
        CreateSubscriptionCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SubscriptionMutationResult<SubscriptionSummary>?> CancelAsync(
        Guid organizationId,
        Guid subscriptionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SubscriptionMutationResult<SubscriptionSummary>?> PauseAsync(
        Guid organizationId,
        Guid subscriptionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SubscriptionMutationResult<SubscriptionSummary>?> ResumeAsync(
        Guid organizationId,
        Guid subscriptionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public interface ISubscriptionPaymentStateService
{
    Task<SubscriptionSummary?> GetAsync(
        Guid organizationId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default);

    Task<SubscriptionSummary?> MarkPastDueAsync(
        Guid organizationId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default);

    Task<SubscriptionSummary?> MarkUnpaidAsync(
        Guid organizationId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default);

    Task<SubscriptionSummary?> RecoverAsync(
        Guid organizationId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default);
}

public sealed record SubscriptionPrice(
    Guid Id,
    string PricingModel,
    string Currency,
    string BillingInterval,
    long? FlatUnitAmountCents,
    long? PerSeatUnitAmountCents,
    long? MeteredUnitAmountCents,
    IReadOnlyList<SubscriptionPriceTier> Tiers,
    int? TrialDays,
    int Version = 1);

public sealed record SubscriptionPriceTier(
    int StartingUnit,
    int? EndingUnit,
    long UnitAmountCents);

public interface ISubscriptionPriceReader
{
    Task<SubscriptionPrice?> GetAsync(
        Guid organizationId,
        Guid priceId,
        CancellationToken cancellationToken = default);
}

public interface ISubscriptionService
{
    Task<SubscriptionMutationResult<SubscriptionSummary>?> CreateAsync(
        Guid organizationId,
        CreateSubscriptionCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionSummary>> ListAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<SubscriptionSummary?> GetAsync(
        Guid organizationId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default);

    Task<SubscriptionProrationReceipt?> PreviewChangeAsync(
        Guid organizationId,
        Guid subscriptionId,
        SubscriptionChangeCommand command,
        CancellationToken cancellationToken = default);

    Task<SubscriptionMutationResult<SubscriptionProrationReceipt>?> ApplyChangeAsync(
        Guid organizationId,
        Guid subscriptionId,
        SubscriptionChangeCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SubscriptionMutationResult<SubscriptionSummary>?> CancelAsync(
        Guid organizationId,
        Guid subscriptionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SubscriptionMutationResult<SubscriptionSummary>?> PauseAsync(
        Guid organizationId,
        Guid subscriptionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SubscriptionMutationResult<SubscriptionSummary>?> ResumeAsync(
        Guid organizationId,
        Guid subscriptionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SubscriptionSummary?> RenewAsync(
        Guid organizationId,
        Guid subscriptionId,
        DateTimeOffset now,
        DateTimeOffset newPeriodEnd,
        CancellationToken cancellationToken = default);
}
