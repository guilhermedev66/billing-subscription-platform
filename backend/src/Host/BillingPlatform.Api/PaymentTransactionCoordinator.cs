using BillingPlatform.Payments.Application;
using BillingPlatform.Billing.Domain;
using BillingPlatform.Webhooks.Application;

namespace BillingPlatform.Api;

internal sealed class PaymentTransactionCoordinator(
    FinancialTransactionCoordinator transactionCoordinator,
    IPaymentService paymentService,
    IWebhookEventWriter webhookEventWriter) : IPaymentTransactionCoordinator
{
    public Task<PaymentResult?> AttemptAsync(
        Guid organizationId,
        Guid invoiceId,
        string cardNumber,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        transactionCoordinator.ExecuteAsync(
            async innerCancellationToken =>
            {
                var result = await paymentService.AttemptAsync(
                    organizationId, invoiceId, cardNumber, idempotencyKey, innerCancellationToken);
                if (result is not null && !result.Replayed)
                {
                    await webhookEventWriter.EnqueueAsync(
                        organizationId, "payment.attempted", "invoice", invoiceId,
                        new { attempt = result.Attempt, invoice = result.Invoice }, innerCancellationToken);
                    if (result.Invoice.Status == InvoiceStatus.Paid)
                    {
                        await webhookEventWriter.EnqueueAsync(
                            organizationId, "invoice.paid", "invoice", invoiceId,
                            new { invoice = result.Invoice }, innerCancellationToken);
                    }
                    if (result.Attempt.Outcome is Payments.Domain.PaymentOutcome.Declined or Payments.Domain.PaymentOutcome.InsufficientFunds)
                    {
                        await webhookEventWriter.EnqueueAsync(
                            organizationId, "dunning.outcome", "invoice", invoiceId,
                            new { attempt = result.Attempt, invoice = result.Invoice }, innerCancellationToken);
                    }
                }

                return result;
            },
            cancellationToken);

    public Task<DunningSweepResponse> SweepDunningAsync(
        Guid organizationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        transactionCoordinator.ExecuteAsync(
            async innerCancellationToken =>
            {
                var result = await paymentService.SweepDunningAsync(
                    organizationId, idempotencyKey, innerCancellationToken);
                if (!result.Replayed)
                {
                    foreach (var attempt in result.Value.Attempts)
                    {
                        await webhookEventWriter.EnqueueAsync(
                            organizationId, "payment.attempted", "invoice", attempt.Attempt.InvoiceId,
                            new { attempt = attempt.Attempt, invoice = attempt.Invoice }, innerCancellationToken);
                        await webhookEventWriter.EnqueueAsync(
                            organizationId, "dunning.outcome", "invoice", attempt.Attempt.InvoiceId,
                            new { attempt = attempt.Attempt, invoice = attempt.Invoice }, innerCancellationToken);
                        if (attempt.Invoice.Status == InvoiceStatus.Paid)
                        {
                            await webhookEventWriter.EnqueueAsync(
                                organizationId, "invoice.paid", "invoice", attempt.Attempt.InvoiceId,
                                new { invoice = attempt.Invoice }, innerCancellationToken);
                        }
                    }
                }

                return result;
            },
            cancellationToken);
}
