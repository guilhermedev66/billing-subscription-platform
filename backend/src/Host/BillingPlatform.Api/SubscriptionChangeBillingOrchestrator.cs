using BillingPlatform.Billing.Application;
using BillingPlatform.Billing.Domain;
using BillingPlatform.Payments.Application;
using BillingPlatform.Subscriptions.Application;
using BillingPlatform.Webhooks.Application;

namespace BillingPlatform.Api;

internal sealed class SubscriptionChangeBillingOrchestrator(
    FinancialTransactionCoordinator transactionCoordinator,
    ISubscriptionService subscriptionService,
    IInvoiceService invoiceService,
    IPaymentService paymentService,
    IWebhookEventWriter webhookEventWriter) : ISubscriptionChangeBillingOrchestrator
{
    public Task<SubscriptionMutationResult<SubscriptionProrationReceipt>?> ApplyAsync(
        Guid organizationId,
        Guid subscriptionId,
        SubscriptionChangeCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        transactionCoordinator.ExecuteAsync(
            async innerCancellationToken =>
            {
                var result = await subscriptionService.ApplyChangeAsync(
                    organizationId,
                    subscriptionId,
                    command,
                    idempotencyKey,
                    innerCancellationToken);
                if (result is null || result.Replayed)
                {
                    return result;
                }

                var billing = await ProcessBillingAsync(
                    organizationId,
                    result.Value,
                    idempotencyKey,
                    command.CardNumber,
                    innerCancellationToken);
                await webhookEventWriter.EnqueueAsync(
                    organizationId, "subscription.changed", "subscription", result.Value.Subscription.Id,
                    new { subscription = result.Value.Subscription, proration = result.Value.Proration },
                    innerCancellationToken);
                if (billing.Invoice is not null)
                {
                    await webhookEventWriter.EnqueueAsync(
                        organizationId, "invoice.created", "invoice", billing.Invoice.Id,
                        new { invoice = billing.Invoice }, innerCancellationToken);
                }
                if (billing.Payment is not null)
                {
                    await webhookEventWriter.EnqueueAsync(
                        organizationId, "payment.attempted", "invoice", billing.Payment.Invoice.Id,
                        new { attempt = billing.Payment.Attempt, invoice = billing.Payment.Invoice },
                        innerCancellationToken);
                    if (billing.Payment.Attempt.Outcome is
                        BillingPlatform.Payments.Domain.PaymentOutcome.Declined or
                        BillingPlatform.Payments.Domain.PaymentOutcome.InsufficientFunds)
                    {
                        await webhookEventWriter.EnqueueAsync(
                            organizationId, "dunning.outcome", "invoice", billing.Payment.Invoice.Id,
                            new { attempt = billing.Payment.Attempt, invoice = billing.Payment.Invoice },
                            innerCancellationToken);
                    }
                }
                var paidInvoice = billing.Payment?.Invoice.Status == InvoiceStatus.Paid
                    ? billing.Payment.Invoice
                    : billing.Invoice?.Status == InvoiceStatus.Paid ? billing.Invoice : null;
                if (paidInvoice is not null)
                {
                    await webhookEventWriter.EnqueueAsync(
                        organizationId, "invoice.paid", "invoice", paidInvoice.Id,
                        new { invoice = paidInvoice }, innerCancellationToken);
                }
                return result;
            },
            cancellationToken);

    private async Task<BillingOutcome> ProcessBillingAsync(
        Guid organizationId,
        SubscriptionProrationReceipt receipt,
        string sourceOperationKey,
        string? cardNumber,
        CancellationToken cancellationToken)
    {
        if (receipt.Proration.AmountDueImmediatelyCents == 0)
        {
            return new BillingOutcome(null, null);
        }

        var lineItems = new List<InvoiceLineItemInput>();
        if (receipt.Proration.ProratedCreditCents < 0)
        {
            lineItems.Add(new InvoiceLineItemInput(
                "Unused time on the current subscription plan",
                receipt.Proration.ProratedCreditCents,
                InvoiceLineType.ProrationCredit));
        }

        if (receipt.Proration.ProratedChargeCents > 0)
        {
            lineItems.Add(new InvoiceLineItemInput(
                "Prorated charge for the new subscription plan",
                receipt.Proration.ProratedChargeCents,
                InvoiceLineType.ProrationDebit));
        }

        var invoice = await invoiceService.CreateOpenAsync(
            organizationId,
            new CreateOpenInvoiceCommand(
                receipt.Subscription.CustomerId,
                receipt.Subscription.Id,
                receipt.Proration.NewPlan.Currency,
                lineItems,
                $"subscription-change:{sourceOperationKey}"),
            cancellationToken);
        if (invoice is null)
        {
            throw new InvalidOperationException("The proration invoice could not be created.");
        }

        if (receipt.Proration.AmountDueImmediatelyCents < 0)
        {
            var settled = await invoiceService.MarkPaidAsync(
                organizationId,
                invoice.Id,
                invoice.IssueDate,
                cancellationToken)
                ?? throw new InvalidOperationException("The credit invoice could not be settled.");
            return new BillingOutcome(settled, null);
        }

        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            throw new ArgumentException(
                "A supported test card is required when the change has an amount due.",
                nameof(cardNumber));
        }

        try
        {
            var payment = await paymentService.AttemptAsync(
                organizationId,
                invoice.Id,
                cardNumber,
                $"subscription-change-payment:{sourceOperationKey}",
                cancellationToken)
                ?? throw new InvalidOperationException("The proration payment could not be created.");
            return new BillingOutcome(invoice, payment);
        }
        catch (PaymentIdempotencyConflictException exception)
        {
            throw new SubscriptionBillingConflictException(exception.Message);
        }
    }

    private sealed record BillingOutcome(InvoiceSummary? Invoice, PaymentResult? Payment);
}
