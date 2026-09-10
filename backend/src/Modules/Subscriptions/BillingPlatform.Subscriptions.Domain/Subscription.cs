namespace BillingPlatform.Subscriptions.Domain;

public sealed class Subscription
{
    private Subscription()
    {
    }

    private Subscription(
        Guid id,
        Guid organizationId,
        Guid customerId,
        Guid priceId,
        SubscriptionStatus status,
        DateTimeOffset currentPeriodStart,
        DateTimeOffset currentPeriodEnd,
        DateTimeOffset? trialEnd,
        int? seatCount,
        DateTimeOffset? canceledAt,
        DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        CustomerId = customerId;
        PriceId = priceId;
        Status = status;
        CurrentPeriodStart = currentPeriodStart;
        CurrentPeriodEnd = currentPeriodEnd;
        TrialEnd = trialEnd;
        SeatCount = seatCount;
        CanceledAt = canceledAt;
        CreatedAt = createdAt;
        Version = 1;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid PriceId { get; private set; }

    public SubscriptionStatus Status { get; private set; }

    public DateTimeOffset CurrentPeriodStart { get; private set; }

    public DateTimeOffset CurrentPeriodEnd { get; private set; }

    public DateTimeOffset? TrialEnd { get; private set; }

    public int? SeatCount { get; private set; }

    public DateTimeOffset? CanceledAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public int Version { get; private set; }

    public static Subscription Create(
        Guid id,
        Guid organizationId,
        Guid customerId,
        Guid priceId,
        SubscriptionStatus status,
        DateTimeOffset currentPeriodStart,
        DateTimeOffset currentPeriodEnd,
        DateTimeOffset? trialEnd,
        int? seatCount,
        DateTimeOffset? canceledAt,
        DateTimeOffset createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(organizationId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(customerId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(priceId, Guid.Empty);

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        var normalizedPeriodStart = currentPeriodStart.ToUniversalTime();
        var normalizedPeriodEnd = currentPeriodEnd.ToUniversalTime();
        if (normalizedPeriodEnd <= normalizedPeriodStart)
        {
            throw new ArgumentException(
                "Current period end must be after current period start.",
                nameof(currentPeriodEnd));
        }

        var normalizedTrialEnd = trialEnd?.ToUniversalTime();
        if (status == SubscriptionStatus.Trialing && normalizedTrialEnd is null)
        {
            throw new ArgumentException(
                "Trialing subscriptions require a trial end date.",
                nameof(trialEnd));
        }

        if (status != SubscriptionStatus.Trialing && normalizedTrialEnd is not null)
        {
            throw new ArgumentException(
                "Only trialing subscriptions can have a trial end date.",
                nameof(trialEnd));
        }

        ValidateSeatCount(seatCount);

        return new Subscription(
            id,
            organizationId,
            customerId,
            priceId,
            status,
            normalizedPeriodStart,
            normalizedPeriodEnd,
            normalizedTrialEnd,
            seatCount,
            canceledAt?.ToUniversalTime(),
            createdAt.ToUniversalTime());
    }

    public void Cancel(DateTimeOffset canceledAt)
    {
        EnsureNotCanceled("cancel");
        Status = SubscriptionStatus.Canceled;
        CanceledAt = canceledAt.ToUniversalTime();
        AdvanceVersion();
    }

    public void Pause()
    {
        if (Status != SubscriptionStatus.Active)
        {
            throw InvalidTransition("pause", "Only active subscriptions can be paused.");
        }

        Status = SubscriptionStatus.Paused;
        AdvanceVersion();
    }

    public void Resume()
    {
        if (Status != SubscriptionStatus.Paused)
        {
            throw InvalidTransition("resume", "Only paused subscriptions can be resumed.");
        }

        Status = SubscriptionStatus.Active;
        AdvanceVersion();
    }

    public void ChangePlan(Guid priceId, int? seatCount)
    {
        EnsureNotCanceled("change plan");
        EnsureStatusIsNot(SubscriptionStatus.Paused, "change plan");
        ArgumentOutOfRangeException.ThrowIfEqual(priceId, Guid.Empty);
        ValidateSeatCount(seatCount);

        PriceId = priceId;
        SeatCount = seatCount;
        AdvanceVersion();
    }

    public void ChangeSeatCount(int seatCount)
    {
        EnsureNotCanceled("change seat count");
        EnsureStatusIsNot(SubscriptionStatus.Paused, "change seat count");
        ValidateSeatCount(seatCount);
        SeatCount = seatCount;
        AdvanceVersion();
    }

    // Reserved for the M4 dunning engine; M3 does not expose these transitions as endpoints.
    public void MarkPastDue()
    {
        if (Status is not (SubscriptionStatus.Active or SubscriptionStatus.Trialing))
        {
            throw InvalidTransition("mark past due", "Only active or trialing subscriptions can become past due.");
        }

        Status = SubscriptionStatus.PastDue;
        AdvanceVersion();
    }

    // Reserved for the M4 dunning engine; M3 does not expose these transitions as endpoints.
    public void MarkUnpaid()
    {
        if (Status != SubscriptionStatus.PastDue)
        {
            throw InvalidTransition("mark unpaid", "Only past-due subscriptions can become unpaid.");
        }

        Status = SubscriptionStatus.Unpaid;
        AdvanceVersion();
    }

    // Reserved for the M4 payment recovery path; M3 does not expose this transition as an endpoint.
    public void Recover()
    {
        if (Status is not (SubscriptionStatus.PastDue or SubscriptionStatus.Unpaid))
        {
            throw InvalidTransition("recover", "Only past-due or unpaid subscriptions can recover.");
        }

        Status = SubscriptionStatus.Active;
        AdvanceVersion();
    }

    private void EnsureNotCanceled(string operation)
    {
        EnsureStatusIsNot(SubscriptionStatus.Canceled, operation);
    }

    private void EnsureStatusIsNot(SubscriptionStatus status, string operation)
    {
        if (Status == status)
        {
            var stateName = status == SubscriptionStatus.Canceled ? "Canceled" : "Paused";
            throw InvalidTransition(operation, $"{stateName} subscriptions cannot {operation}.");
        }
    }

    private static SubscriptionDomainException InvalidTransition(
        string operation,
        string message) =>
        new($"Cannot {operation} subscription. {message}");

    private static void ValidateSeatCount(int? seatCount)
    {
        if (seatCount is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seatCount),
                "Seat count must be greater than zero when provided.");
        }
    }

    private void AdvanceVersion()
    {
        checked
        {
            Version++;
        }
    }
}
