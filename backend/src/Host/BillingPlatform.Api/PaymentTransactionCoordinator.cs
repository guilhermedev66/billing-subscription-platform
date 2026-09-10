using BillingPlatform.Payments.Application;

namespace BillingPlatform.Api;

internal sealed class PaymentTransactionCoordinator(
    FinancialTransactionCoordinator transactionCoordinator,
    IPaymentService paymentService) : IPaymentTransactionCoordinator
{
    public Task<PaymentResult?> AttemptAsync(
        Guid organizationId,
        Guid invoiceId,
        string cardNumber,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        transactionCoordinator.ExecuteAsync(
            innerCancellationToken => paymentService.AttemptAsync(
                organizationId,
                invoiceId,
                cardNumber,
                idempotencyKey,
                innerCancellationToken),
            cancellationToken);

    public Task<DunningSweepResponse> SweepDunningAsync(
        Guid organizationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        transactionCoordinator.ExecuteAsync(
            innerCancellationToken => paymentService.SweepDunningAsync(
                organizationId,
                idempotencyKey,
                innerCancellationToken),
            cancellationToken);
}
