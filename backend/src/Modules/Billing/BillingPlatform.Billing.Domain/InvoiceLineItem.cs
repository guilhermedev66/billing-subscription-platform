namespace BillingPlatform.Billing.Domain;

public sealed class InvoiceLineItem
{
    private InvoiceLineItem()
    {
    }

    private InvoiceLineItem(
        Guid id,
        string description,
        long amountCents,
        InvoiceLineType lineType)
    {
        Id = id;
        Description = description;
        AmountCents = amountCents;
        LineType = lineType;
    }

    public Guid Id { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public long AmountCents { get; private set; }

    public InvoiceLineType LineType { get; private set; }

    internal static InvoiceLineItem Create(
        Guid id,
        string description,
        long amountCents,
        InvoiceLineType lineType)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (description.Trim().Length > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(description));
        }

        if (!Enum.IsDefined(lineType))
        {
            throw new ArgumentOutOfRangeException(nameof(lineType));
        }

        return new InvoiceLineItem(id, description.Trim(), amountCents, lineType);
    }
}
