using BillingPlatform.Subscriptions.Application;
using BillingPlatform.Subscriptions.Domain;

namespace BillingPlatform.UnitTests;

public sealed class SubscriptionsDomainTests
{
    [Fact]
    public void Cancel_transitions_an_active_subscription_and_records_time()
    {
        var subscription = CreateActiveSubscription();
        var canceledAt = PeriodStart.AddDays(3);

        subscription.Cancel(canceledAt);

        Assert.Equal(SubscriptionStatus.Canceled, subscription.Status);
        Assert.Equal(canceledAt, subscription.CanceledAt);
        Assert.Equal(2, subscription.Version);
    }

    [Fact]
    public void Pause_and_resume_follow_the_guarded_state_machine()
    {
        var subscription = CreateActiveSubscription();

        subscription.Pause();
        Assert.Equal(SubscriptionStatus.Paused, subscription.Status);

        subscription.Resume();
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public void Plan_and_seat_changes_update_the_subscription()
    {
        var newPriceId = Guid.NewGuid();
        var subscription = CreateActiveSubscription();

        subscription.ChangePlan(newPriceId, 4);

        Assert.Equal(newPriceId, subscription.PriceId);
        Assert.Equal(4, subscription.SeatCount);
        Assert.Equal(2, subscription.Version);
    }

    [Fact]
    public void Dunning_states_can_progress_and_recover()
    {
        var subscription = CreateActiveSubscription();

        subscription.MarkPastDue();
        subscription.MarkUnpaid();
        subscription.Recover();

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public void Invalid_state_transitions_throw_domain_exceptions()
    {
        var active = CreateActiveSubscription();
        Assert.Throws<SubscriptionDomainException>(() => active.Resume());
        Assert.Throws<SubscriptionDomainException>(() => active.MarkUnpaid());

        active.Cancel(PeriodStart);
        Assert.Throws<SubscriptionDomainException>(() => active.Cancel(PeriodStart));
        Assert.Throws<SubscriptionDomainException>(() => active.ChangePlan(Guid.NewGuid(), null));

        var paused = CreateActiveSubscription();
        paused.Pause();
        Assert.Throws<SubscriptionDomainException>(() => paused.Pause());
        Assert.Throws<SubscriptionDomainException>(() => paused.ChangePlan(Guid.NewGuid(), null));
        Assert.Throws<SubscriptionDomainException>(() => paused.ChangeSeatCount(3));
        paused.Resume();
        paused.ChangePlan(Guid.NewGuid(), null);

        var trialing = CreateTrialingSubscription();
        Assert.Throws<SubscriptionDomainException>(() => trialing.Pause());
    }

    [Fact]
    public void Proration_at_cycle_start_uses_the_full_remaining_cycle()
    {
        var result = Calculate(1500, 4500, PeriodStart);

        Assert.Equal(30, result.RemainingCycleDays);
        Assert.Equal(100m, result.RemainingCyclePercentage);
        Assert.Equal(-1500L, result.ProratedCreditCents);
        Assert.Equal(4500L, result.ProratedChargeCents);
        Assert.Equal(3000L, result.AmountDueImmediatelyCents);
        Assert.Equal(4500L, result.NextRegularRenewalAmountCents);
    }

    [Fact]
    public void Proration_mid_cycle_calculates_credit_and_charge_in_integer_cents()
    {
        var result = Calculate(1500, 4500, PeriodStart.AddDays(15));

        Assert.Equal(15, result.RemainingCycleDays);
        Assert.Equal(50m, result.RemainingCyclePercentage);
        Assert.Equal(-750L, result.ProratedCreditCents);
        Assert.Equal(2250L, result.ProratedChargeCents);
        Assert.Equal(1500L, result.AmountDueImmediatelyCents);
    }

    [Fact]
    public void Proration_on_the_last_day_has_one_day_remaining_and_end_has_none()
    {
        var lastDay = Calculate(1000, 2000, PeriodStart.AddDays(29));
        var atEnd = Calculate(1000, 2000, PeriodStart.AddDays(30));

        Assert.Equal(1, lastDay.RemainingCycleDays);
        Assert.Equal(3.33m, lastDay.RemainingCyclePercentage);
        Assert.Equal(-33L, lastDay.ProratedCreditCents);
        Assert.Equal(67L, lastDay.ProratedChargeCents);
        Assert.Equal(34L, lastDay.AmountDueImmediatelyCents);
        Assert.Equal(0, atEnd.RemainingCycleDays);
        Assert.Equal(0m, atEnd.RemainingCyclePercentage);
        Assert.Equal(0L, atEnd.AmountDueImmediatelyCents);
    }

    [Fact]
    public void Proration_supports_a_more_expensive_new_plan_and_a_downgrade()
    {
        var upgrade = Calculate(1000, 4000, PeriodStart.AddDays(10));
        var downgrade = Calculate(4000, 1000, PeriodStart.AddDays(10));

        Assert.Equal(2667L, upgrade.ProratedChargeCents);
        Assert.Equal(-667L, upgrade.ProratedCreditCents);
        Assert.Equal(2000L, upgrade.AmountDueImmediatelyCents);
        Assert.Equal(-2000L, downgrade.AmountDueImmediatelyCents);
    }

    [Fact]
    public void Proration_rounding_is_deterministic_for_fractional_cents()
    {
        var result = Calculate(100, 0, PeriodStart.AddDays(1));

        Assert.Equal(-97L, result.ProratedCreditCents);
        Assert.Equal(0L, result.ProratedChargeCents);
        Assert.Equal(-97L, result.AmountDueImmediatelyCents);
    }

    [Fact]
    public void Proration_rejects_an_invalid_period_or_negative_amount()
    {
        var calculator = new ProrationCalculator();
        var oldPlan = new ProrationPlan(Guid.NewGuid(), "USD", 1000);
        var newPlan = new ProrationPlan(Guid.NewGuid(), "USD", 2000);

        Assert.Throws<ArgumentException>(() => calculator.Calculate(
            new ProrationInput(oldPlan, newPlan, PeriodStart, PeriodStart, PeriodStart)));
        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.Calculate(
            new ProrationInput(
                oldPlan with { RecurringAmountCents = -1 },
                newPlan,
                PeriodStart,
                PeriodEnd,
                PeriodStart)));
    }

    [Fact]
    public void Trial_proration_preserves_plan_amounts_but_charges_nothing_immediately()
    {
        var calculator = new ProrationCalculator();
        var result = calculator.CalculateNoCharge(
            new ProrationInput(
                new ProrationPlan(Guid.NewGuid(), "USD", 2000),
                new ProrationPlan(Guid.NewGuid(), "USD", 4500),
                PeriodStart,
                PeriodEnd,
                PeriodStart.AddDays(10)));

        Assert.Equal(2000L, result.CurrentPlan.RecurringAmountCents);
        Assert.Equal(4500L, result.NewPlan.RecurringAmountCents);
        Assert.Equal(0L, result.ProratedCreditCents);
        Assert.Equal(0L, result.ProratedChargeCents);
        Assert.Equal(0L, result.AmountDueImmediatelyCents);
        Assert.Equal(4500L, result.NextRegularRenewalAmountCents);
    }

    private static readonly DateTimeOffset PeriodStart =
        new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset PeriodEnd = PeriodStart.AddDays(30);

    private static Subscription CreateActiveSubscription() =>
        Subscription.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            SubscriptionStatus.Active,
            PeriodStart,
            PeriodEnd,
            null,
            null,
            null,
            PeriodStart);

    private static Subscription CreateTrialingSubscription() =>
        Subscription.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            SubscriptionStatus.Trialing,
            PeriodStart,
            PeriodEnd,
            PeriodStart.AddDays(14),
            null,
            null,
            PeriodStart);

    private static ProrationResult Calculate(
        long currentAmount,
        long newAmount,
        DateTimeOffset now) =>
        new ProrationCalculator().Calculate(
            new ProrationInput(
                new ProrationPlan(Guid.NewGuid(), "USD", currentAmount),
                new ProrationPlan(Guid.NewGuid(), "USD", newAmount),
                PeriodStart,
                PeriodEnd,
                now));
}
