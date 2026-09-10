using BillingPlatform.Billing.Application;
using BillingPlatform.Payments.Domain;

namespace BillingPlatform.Payments.Application;

public sealed class PaymentIdempotencyConflictException()
    : Exception("The Idempotency-Key was already used with a different payment request.");

public sealed class PaymentConcurrencyException()
    : Exception("The payment was modified by another request.");

public sealed class PaymentStateConflictException(string message) : Exception(message);

public sealed class PaymentProcessingException()
    : Exception("The simulated payment gateway is temporarily unavailable.");

public sealed record PaymentAttemptSummary(
    Guid Id,
    Guid OrganizationId,
    Guid InvoiceId,
    string CardNumberLast4,
    PaymentOutcome Outcome,
    DateTimeOffset AttemptedAt,
    int AttemptNumber);

public sealed record PaymentResult(
    PaymentAttemptSummary Attempt,
    InvoiceSummary Invoice,
    bool Replayed,
    string ResponseBody);

public sealed record PaymentRequest(string CardNumber);

public sealed record DunningSweepResult(
    int Considered,
    int Processed,
    int Succeeded,
    int Failed,
    int BecameUncollectible,
    IReadOnlyList<PaymentResult> Attempts);

public sealed record DunningSweepResponse(
    DunningSweepResult Value,
    bool Replayed,
    string ResponseBody);

public interface IPaymentService
{
    Task<IReadOnlyList<PaymentAttemptSummary>?> ListAttemptsAsync(
        Guid organizationId,
        Guid invoiceId,
        CancellationToken cancellationToken = default);

    Task<PaymentResult?> AttemptAsync(
        Guid organizationId,
        Guid invoiceId,
        string cardNumber,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<DunningSweepResponse> SweepDunningAsync(
        Guid organizationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public interface IPaymentTransactionCoordinator
{
    Task<PaymentResult?> AttemptAsync(
        Guid organizationId,
        Guid invoiceId,
        string cardNumber,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<DunningSweepResponse> SweepDunningAsync(
        Guid organizationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}
