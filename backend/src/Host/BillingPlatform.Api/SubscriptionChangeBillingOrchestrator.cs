using BillingPlatform.Billing.Application;
using BillingPlatform.Billing.Domain;
using BillingPlatform.Payments.Application;
using BillingPlatform.Subscriptions.Application;

namespace BillingPlatform.Api;

internal sealed class SubscriptionChangeBillingOrchestrator(
    FinancialTransactionCoordinator transactionCoordinator,
    ISubscriptionService subscriptionService,
    IInvoiceService invoiceService,
    IPaymentService paymentService) : ISubscriptionChangeBillingOrchestrator
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

                await ProcessBillingAsync(
                    organizationId,
                    result.Value,
                    idempotencyKey,
                    command.CardNumber,
                    innerCancellationToken);
                return result;
            },
            cancellationToken);

    private async Task ProcessBillingAsync(
        Guid organizationId,
        SubscriptionProrationReceipt receipt,
        string sourceOperationKey,
        string? cardNumber,
        CancellationToken cancellationToken)
    {
        if (receipt.Proration.AmountDueImmediatelyCents == 0)
        {
            return;
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
            _ = await invoiceService.MarkPaidAsync(
                organizationId,
                invoice.Id,
                invoice.IssueDate,
                cancellationToken)
                ?? throw new InvalidOperationException("The credit invoice could not be settled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            throw new ArgumentException(
                "A supported test card is required when the change has an amount due.",
                nameof(cardNumber));
        }

        try
        {
            _ = await paymentService.AttemptAsync(
                organizationId,
                invoice.Id,
                cardNumber,
                $"subscription-change-payment:{sourceOperationKey}",
                cancellationToken)
                ?? throw new InvalidOperationException("The proration payment could not be created.");
        }
        catch (PaymentIdempotencyConflictException exception)
        {
            throw new SubscriptionBillingConflictException(exception.Message);
        }
    }
}
