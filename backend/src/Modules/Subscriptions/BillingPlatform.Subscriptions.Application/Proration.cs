using System.Numerics;

namespace BillingPlatform.Subscriptions.Application;

public sealed record ProrationPlan(
    Guid PriceId,
    string Currency,
    long RecurringAmountCents);

public sealed record ProrationInput(
    ProrationPlan CurrentPlan,
    ProrationPlan NewPlan,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    DateTimeOffset Now);

public sealed record ProrationResult(
    ProrationPlan CurrentPlan,
    ProrationPlan NewPlan,
    int RemainingCycleDays,
    decimal RemainingCyclePercentage,
    long ProratedCreditCents,
    long ProratedChargeCents,
    long AmountDueImmediatelyCents,
    long NextRegularRenewalAmountCents);

public interface IProrationCalculator
{
    ProrationResult Calculate(ProrationInput input);

    ProrationResult CalculateNoCharge(ProrationInput input);
}

public sealed class ProrationCalculator : IProrationCalculator
{
    public ProrationResult Calculate(ProrationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.CurrentPlan);
        ArgumentNullException.ThrowIfNull(input.NewPlan);

        if (input.PeriodEnd <= input.PeriodStart)
        {
            throw new ArgumentException("Period end must be after period start.", nameof(input));
        }

        ValidateAmount(input.CurrentPlan.RecurringAmountCents, nameof(input.CurrentPlan));
        ValidateAmount(input.NewPlan.RecurringAmountCents, nameof(input.NewPlan));

        var durationTicks = input.PeriodEnd.UtcTicks - input.PeriodStart.UtcTicks;
        var remainingTicks = GetRemainingTicks(input);
        var remainingDays = CeilingDivide(remainingTicks, TimeSpan.TicksPerDay);
        var remainingPercentage = decimal.Round(
            (decimal)remainingTicks * 100m / durationTicks,
            2,
            MidpointRounding.AwayFromZero);

        var creditMagnitude = MultiplyAndRound(
            input.CurrentPlan.RecurringAmountCents,
            remainingTicks,
            durationTicks);
        var charge = MultiplyAndRound(
            input.NewPlan.RecurringAmountCents,
            remainingTicks,
            durationTicks);
        var credit = checked(-creditMagnitude);

        return new ProrationResult(
            input.CurrentPlan,
            input.NewPlan,
            remainingDays,
            remainingPercentage,
            credit,
            charge,
            checked(credit + charge),
            input.NewPlan.RecurringAmountCents);
    }

    public ProrationResult CalculateNoCharge(ProrationInput input)
    {
        var result = Calculate(input);
        return result with
        {
            ProratedCreditCents = 0,
            ProratedChargeCents = 0,
            AmountDueImmediatelyCents = 0,
            NextRegularRenewalAmountCents = input.NewPlan.RecurringAmountCents
        };
    }

    private static long GetRemainingTicks(ProrationInput input)
    {
        if (input.Now <= input.PeriodStart)
        {
            return input.PeriodEnd.UtcTicks - input.PeriodStart.UtcTicks;
        }

        if (input.Now >= input.PeriodEnd)
        {
            return 0;
        }

        return input.PeriodEnd.UtcTicks - input.Now.UtcTicks;
    }

    private static long MultiplyAndRound(long amount, long numerator, long denominator)
    {
        var product = (BigInteger)amount * numerator;
        var quotient = BigInteger.DivRem(product, denominator, out var remainder);
        if (remainder * 2 >= denominator)
        {
            quotient++;
        }

        return checked((long)quotient);
    }

    private static int CeilingDivide(long numerator, long denominator)
    {
        if (numerator == 0)
        {
            return 0;
        }

        return checked((int)((numerator + denominator - 1) / denominator));
    }

    private static void ValidateAmount(long amount, string parameterName)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Recurring amounts must be non-negative integer cents.");
        }
    }
}
