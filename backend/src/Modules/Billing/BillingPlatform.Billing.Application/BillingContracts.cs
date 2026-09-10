using BillingPlatform.Billing.Domain;

namespace BillingPlatform.Billing.Application;

public sealed class InvoiceConcurrencyException()
    : Exception("The invoice was modified by another request.");

public sealed record InvoiceLineItemInput(
    string Description,
    long AmountCents,
    InvoiceLineType LineType);

public sealed record CreateOpenInvoiceCommand(
    Guid CustomerId,
    Guid? SubscriptionId,
    string Currency,
    IReadOnlyList<InvoiceLineItemInput> LineItems,
    string SourceOperationKey,
    DateTimeOffset? DueDate = null);

public sealed record InvoiceLineItemSummary(
    Guid Id,
    string Description,
    long AmountCents,
    InvoiceLineType LineType);

public sealed record InvoiceSummary(
    Guid Id,
    Guid OrganizationId,
    Guid CustomerId,
    Guid? SubscriptionId,
    InvoiceStatus Status,
    string InvoiceNumber,
    string Currency,
    IReadOnlyList<InvoiceLineItemSummary> LineItems,
    long Subtotal,
    long Total,
    DateTimeOffset IssueDate,
    DateTimeOffset DueDate,
    DateTimeOffset? PaidAt,
    DateTimeOffset CreatedAt,
    int DunningAttemptCount,
    DateTimeOffset? NextRetryAt);

public interface IInvoiceService
{
    Task<InvoiceSummary?> CreateOpenAsync(
        Guid organizationId,
        CreateOpenInvoiceCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InvoiceSummary>> ListAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<InvoiceSummary?> GetAsync(
        Guid organizationId,
        Guid invoiceId,
        CancellationToken cancellationToken = default);

    Task<InvoiceSummary?> VoidAsync(
        Guid organizationId,
        Guid invoiceId,
        CancellationToken cancellationToken = default);

    Task<InvoiceSummary?> MarkPaidAsync(
        Guid organizationId,
        Guid invoiceId,
        DateTimeOffset paidAt,
        CancellationToken cancellationToken = default);

    Task<InvoiceSummary?> RecordPaymentFailureAsync(
        Guid organizationId,
        Guid invoiceId,
        DateTimeOffset attemptedAt,
        CancellationToken cancellationToken = default);
}
