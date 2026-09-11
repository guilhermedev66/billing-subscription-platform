using BillingPlatform.Billing.Application;
using BillingPlatform.Billing.Domain;
using BillingPlatform.Payments.Application;
using BillingPlatform.SimulationClock.Application;
using BillingPlatform.Subscriptions.Application;
using BillingPlatform.Subscriptions.Domain;
using BillingPlatform.Webhooks.Application;

namespace BillingPlatform.Api;

internal sealed class RenewalCronService(
    FinancialTransactionCoordinator transactionCoordinator,
    ISubscriptionService subscriptionService,
    ISubscriptionPriceReader priceReader,
    IInvoiceService invoiceService,
    IPaymentService paymentService,
    IWebhookEventWriter webhookEventWriter,
    IVirtualClock clock)
{
    public async Task<RenewalCronResult> RunAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var now = clock.Now.ToUniversalTime();
        var dueSubscriptions = (await subscriptionService.ListAsync(organizationId, cancellationToken))
            .Where(subscription => subscription.Status == SubscriptionStatus.Active)
            .Where(subscription => subscription.CurrentPeriodEnd <= now)
            .ToList();
        var renewed = new List<RenewalResult>();
        foreach (var due in dueSubscriptions)
        {
            try
            {
                var result = await transactionCoordinator.ExecuteAsync(
                    innerCancellationToken => RenewOneAsync(organizationId, due, innerCancellationToken),
                    cancellationToken);
                if (result is not null)
                {
                    renewed.Add(result);
                }
            }
            catch (SubscriptionConcurrencyException)
            {
                // Another worker renewed this period first. The unique source key and
                // optimistic subscription version make this a safe no-op.
            }
        }

        return new RenewalCronResult(dueSubscriptions.Count, renewed);
    }

    private async Task<RenewalResult?> RenewOneAsync(
        Guid organizationId,
        SubscriptionSummary due,
        CancellationToken cancellationToken)
    {
        var price = await priceReader.GetAsync(organizationId, due.PriceId, cancellationToken)
            ?? throw new ArgumentException("The subscription price no longer exists.");
        var newPeriodEnd = string.Equals(price.BillingInterval, "Month", StringComparison.Ordinal)
            ? due.CurrentPeriodEnd.AddMonths(1)
            : due.CurrentPeriodEnd.AddYears(1);
        var renewed = await subscriptionService.RenewAsync(
            organizationId, due.Id, clock.Now, newPeriodEnd, cancellationToken);
        if (renewed is null)
        {
            return null;
        }

        var amount = ResolveRecurringAmount(price, due.SeatCount);
        var sourceKey = $"renewal-{due.Id:D}-{due.CurrentPeriodEnd.UtcTicks}";
        var invoice = await invoiceService.CreateOpenAsync(
            organizationId,
            new CreateOpenInvoiceCommand(
                due.CustomerId,
                due.Id,
                price.Currency,
                [new InvoiceLineItemInput(
                    $"Recurring subscription renewal ({due.CurrentPeriodEnd:yyyy-MM-dd})",
                    amount,
                    InvoiceLineType.Base)],
                sourceKey),
            cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var payment = await paymentService.AttemptAsync(
            organizationId,
            invoice.Id,
            "4242424242424242",
            $"{sourceKey}-payment",
            cancellationToken);
        if (payment is null)
        {
            return null;
        }

        await webhookEventWriter.EnqueueAsync(
            organizationId, "subscription.renewed", "subscription", due.Id,
            new { subscription = renewed }, cancellationToken);
        await webhookEventWriter.EnqueueAsync(
            organizationId, "invoice.created", "invoice", invoice.Id,
            new { invoice }, cancellationToken);
        await webhookEventWriter.EnqueueAsync(
            organizationId, "payment.attempted", "invoice", invoice.Id,
            new { attempt = payment.Attempt, invoice = payment.Invoice }, cancellationToken);
        if (payment.Invoice.Status == InvoiceStatus.Paid)
        {
            await webhookEventWriter.EnqueueAsync(
                organizationId, "invoice.paid", "invoice", invoice.Id,
                new { invoice = payment.Invoice }, cancellationToken);
        }

        return new RenewalResult(renewed, payment);
    }

    private static long ResolveRecurringAmount(SubscriptionPrice price, int? seatCount) =>
        price.PricingModel switch
        {
            "Flat" => price.FlatUnitAmountCents ?? throw new ArgumentException("Flat price is missing amount."),
            "PerSeat" => checked((price.PerSeatUnitAmountCents ?? throw new ArgumentException("Per-seat price is missing amount.")) * (seatCount ?? throw new ArgumentException("Seat count is required."))),
            "Tiered" => CalculateTieredAmount(price, seatCount ?? throw new ArgumentException("Seat count is required.")),
            "Metered" => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(price.PricingModel))
        };

    private static long CalculateTieredAmount(SubscriptionPrice price, int seatCount)
    {
        long total = 0;
        foreach (var tier in price.Tiers.OrderBy(tier => tier.StartingUnit))
        {
            if (tier.StartingUnit > seatCount)
            {
                break;
            }

            var end = Math.Min(tier.EndingUnit ?? seatCount, seatCount);
            if (end >= tier.StartingUnit)
            {
                total = checked(total + checked((end - tier.StartingUnit + 1) * tier.UnitAmountCents));
            }
        }

        return total;
    }
}

internal sealed record RenewalCronResult(int Considered, IReadOnlyList<RenewalResult> Renewed);

internal sealed record RenewalResult(SubscriptionSummary Subscription, PaymentResult Payment);
