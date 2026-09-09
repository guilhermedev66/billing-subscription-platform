namespace BillingPlatform.Organizations.Domain;

public sealed class Organization
{
    private Organization()
    {
    }

    private Organization(
        Guid id,
        string name,
        string defaultCurrency,
        string invoicePrefix,
        string webhookSecret,
        bool simulationModeEnabled,
        DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        DefaultCurrency = defaultCurrency;
        InvoicePrefix = invoicePrefix;
        WebhookSecret = webhookSecret;
        SimulationModeEnabled = simulationModeEnabled;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string DefaultCurrency { get; private set; } = string.Empty;

    public string InvoicePrefix { get; private set; } = string.Empty;

    public string WebhookSecret { get; private set; } = string.Empty;

    public bool SimulationModeEnabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Organization Create(
        Guid id,
        string name,
        string defaultCurrency,
        string invoicePrefix,
        string webhookSecret,
        bool simulationModeEnabled,
        DateTimeOffset createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultCurrency);
        ArgumentException.ThrowIfNullOrWhiteSpace(invoicePrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(webhookSecret);

        var normalizedName = name.Trim();
        var normalizedCurrency = defaultCurrency.Trim().ToUpperInvariant();
        var normalizedPrefix = invoicePrefix.Trim().ToUpperInvariant();

        if (normalizedName.Length > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(name), "Name cannot exceed 200 characters.");
        }

        if (normalizedCurrency.Length != 3 || !normalizedCurrency.All(char.IsAsciiLetter))
        {
            throw new ArgumentException(
                "Default currency must be a three-letter ISO currency code.",
                nameof(defaultCurrency));
        }

        if (normalizedPrefix.Length > 16 ||
            !normalizedPrefix.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
        {
            throw new ArgumentException(
                "Invoice prefix must be at most 16 letters, digits, or hyphens.",
                nameof(invoicePrefix));
        }

        return new Organization(
            id,
            normalizedName,
            normalizedCurrency,
            normalizedPrefix,
            webhookSecret,
            simulationModeEnabled,
            createdAt.ToUniversalTime());
    }
}
