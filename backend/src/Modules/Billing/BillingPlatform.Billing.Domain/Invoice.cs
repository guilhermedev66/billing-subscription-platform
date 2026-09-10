namespace BillingPlatform.Billing.Domain;

public sealed class Invoice
{
    private readonly List<InvoiceLineItem> lineItems = [];

    private Invoice()
    {
    }

    private Invoice(
        Guid id,
        Guid organizationId,
        Guid customerId,
        Guid? subscriptionId,
        string invoiceNumber,
        string currency,
        DateTimeOffset issueDate,
        DateTimeOffset dueDate,
        DateTimeOffset createdAt,
        string? sourceOperationKey)
    {
        Id = id;
        OrganizationId = organizationId;
        CustomerId = customerId;
        SubscriptionId = subscriptionId;
        InvoiceNumber = invoiceNumber;
        Currency = currency;
        Status = InvoiceStatus.Draft;
        IssueDate = issueDate;
        DueDate = dueDate;
        CreatedAt = createdAt;
        SourceOperationKey = sourceOperationKey;
        Version = 1;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid? SubscriptionId { get; private set; }

    public InvoiceStatus Status { get; private set; }

    public string InvoiceNumber { get; private set; } = string.Empty;

    public string Currency { get; private set; } = string.Empty;

    public IReadOnlyCollection<InvoiceLineItem> LineItems => lineItems.AsReadOnly();

    public long Subtotal => lineItems.Aggregate(0L, (total, item) => checked(total + item.AmountCents));

    public long Total => Subtotal;

    public DateTimeOffset IssueDate { get; private set; }

    public DateTimeOffset DueDate { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public int DunningAttemptCount { get; private set; }

    public DateTimeOffset? DunningAnchorAt { get; private set; }

    public DateTimeOffset? NextRetryAt { get; private set; }

    public string? SourceOperationKey { get; private set; }

    public int Version { get; private set; }

    public static Invoice Create(
        Guid id,
        Guid organizationId,
        Guid customerId,
        Guid? subscriptionId,
        string invoiceNumber,
        string currency,
        DateTimeOffset issueDate,
        DateTimeOffset dueDate,
        DateTimeOffset createdAt,
        string? sourceOperationKey = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(organizationId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(customerId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        if (invoiceNumber.Trim().Length > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(invoiceNumber));
        }

        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3 || !normalizedCurrency.All(char.IsAsciiLetter))
        {
            throw new ArgumentException("Currency must be a three-letter ISO currency code.", nameof(currency));
        }

        var normalizedIssueDate = issueDate.ToUniversalTime();
        var normalizedDueDate = dueDate.ToUniversalTime();
        if (normalizedDueDate < normalizedIssueDate)
        {
            throw new ArgumentException("Due date cannot be before issue date.", nameof(dueDate));
        }

        if (sourceOperationKey?.Length > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceOperationKey));
        }

        return new Invoice(
            id,
            organizationId,
            customerId,
            subscriptionId,
            invoiceNumber.Trim(),
            normalizedCurrency,
            normalizedIssueDate,
            normalizedDueDate,
            createdAt.ToUniversalTime(),
            sourceOperationKey);
    }

    public void AddLineItem(
        string description,
        long amountCents,
        InvoiceLineType lineType)
    {
        EnsureDraft("add line items");
        lineItems.Add(InvoiceLineItem.Create(Guid.NewGuid(), description, amountCents, lineType));
    }

    public void Open()
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw InvalidTransition("open", "Only draft invoices can be opened.");
        }

        if (lineItems.Count == 0)
        {
            throw new InvalidOperationException("An invoice must contain at least one line item.");
        }

        Status = InvoiceStatus.Open;
        AdvanceVersion();
    }

    public void MarkPaid(DateTimeOffset paidAt)
    {
        if (Status == InvoiceStatus.Paid)
        {
            return;
        }

        EnsureOpen("mark paid");
        Status = InvoiceStatus.Paid;
        PaidAt = paidAt.ToUniversalTime();
        NextRetryAt = null;
        AdvanceVersion();
    }

    public void MarkVoid()
    {
        if (Status != InvoiceStatus.Draft && Status != InvoiceStatus.Open)
        {
            throw InvalidTransition("void", "Only draft or open invoices can be voided.");
        }

        Status = InvoiceStatus.Void;
        NextRetryAt = null;
        AdvanceVersion();
    }

    public void MarkUncollectible()
    {
        if (Status == InvoiceStatus.Uncollectible)
        {
            return;
        }

        EnsureOpen("mark uncollectible");
        Status = InvoiceStatus.Uncollectible;
        NextRetryAt = null;
        AdvanceVersion();
    }

    public void RecordPaymentFailure(DateTimeOffset attemptedAt)
    {
        EnsureOpen("record a payment failure");
        var normalizedAttemptedAt = attemptedAt.ToUniversalTime();
        if (DunningAttemptCount == 0)
        {
            DunningAttemptCount = 1;
            DunningAnchorAt = normalizedAttemptedAt;
            NextRetryAt = normalizedAttemptedAt.AddDays(3);
            AdvanceVersion();
            return;
        }

        DunningAttemptCount = checked(DunningAttemptCount + 1);
        NextRetryAt = DunningAttemptCount switch
        {
            2 => DunningAnchorAt!.Value.AddDays(7),
            3 => DunningAnchorAt!.Value.AddDays(14),
            _ => null
        };

        if (DunningAttemptCount >= 4)
        {
            MarkUncollectible();
        }
        else
        {
            AdvanceVersion();
        }
    }

    private void EnsureDraft(string operation)
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw InvalidTransition(operation, "Only draft invoices can be changed.");
        }
    }

    private void EnsureOpen(string operation)
    {
        if (Status != InvoiceStatus.Open)
        {
            throw InvalidTransition(operation, "Only open invoices can be charged.");
        }
    }

    private static InvoiceDomainException InvalidTransition(string operation, string message) =>
        new($"Cannot {operation} invoice. {message}");

    private void AdvanceVersion()
    {
        checked
        {
            Version++;
        }
    }
}
