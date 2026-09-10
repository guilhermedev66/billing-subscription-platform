namespace BillingPlatform.Payments.Domain;

public sealed class PaymentAttempt
{
    private PaymentAttempt()
    {
    }

    private PaymentAttempt(
        Guid id,
        Guid organizationId,
        Guid invoiceId,
        string cardNumberLast4,
        PaymentOutcome outcome,
        DateTimeOffset attemptedAt,
        string operation,
        string idempotencyKey,
        string requestHash,
        string responseBody,
        int responseStatusCode,
        int attemptNumber)
    {
        Id = id;
        OrganizationId = organizationId;
        InvoiceId = invoiceId;
        CardNumberLast4 = cardNumberLast4;
        Outcome = outcome;
        AttemptedAt = attemptedAt;
        Operation = operation;
        IdempotencyKey = idempotencyKey;
        RequestHash = requestHash;
        ResponseBody = responseBody;
        ResponseStatusCode = responseStatusCode;
        AttemptNumber = attemptNumber;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid InvoiceId { get; private set; }

    public string CardNumberLast4 { get; private set; } = string.Empty;

    public PaymentOutcome Outcome { get; private set; }

    public DateTimeOffset AttemptedAt { get; private set; }

    public string Operation { get; private set; } = string.Empty;

    public string IdempotencyKey { get; private set; } = string.Empty;

    public string RequestHash { get; private set; } = string.Empty;

    public string ResponseBody { get; private set; } = string.Empty;

    public int ResponseStatusCode { get; private set; }

    public int AttemptNumber { get; private set; }

    public void Complete(string responseBody)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(responseBody);
        ResponseBody = responseBody;
    }

    public static PaymentAttempt Create(
        Guid id,
        Guid organizationId,
        Guid invoiceId,
        string cardNumberLast4,
        PaymentOutcome outcome,
        DateTimeOffset attemptedAt,
        string operation,
        string idempotencyKey,
        string requestHash,
        string responseBody,
        int responseStatusCode,
        int attemptNumber)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(organizationId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(invoiceId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(cardNumberLast4);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(responseBody);
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        if (cardNumberLast4.Length != 4 || !cardNumberLast4.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Card last four must contain exactly four digits.", nameof(cardNumberLast4));
        }

        if (attemptNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        }

        return new PaymentAttempt(
            id,
            organizationId,
            invoiceId,
            cardNumberLast4,
            outcome,
            attemptedAt.ToUniversalTime(),
            operation,
            idempotencyKey,
            requestHash,
            responseBody,
            responseStatusCode,
            attemptNumber);
    }
}
