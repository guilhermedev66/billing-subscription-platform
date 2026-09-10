namespace BillingPlatform.Customers.Domain;

public sealed class Customer
{
    private Customer()
    {
    }

    private Customer(
        Guid id,
        Guid organizationId,
        string name,
        string email,
        long balanceCents,
        bool delinquentFlag,
        DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
        Email = email;
        BalanceCents = balanceCents;
        DelinquentFlag = delinquentFlag;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public long BalanceCents { get; private set; }

    public bool DelinquentFlag { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Customer Create(
        Guid id,
        Guid organizationId,
        string name,
        string email,
        long balanceCents,
        bool delinquentFlag,
        DateTimeOffset createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(organizationId, Guid.Empty);

        var normalizedName = NormalizeName(name);
        var normalizedEmail = NormalizeEmail(email);

        return new Customer(
            id,
            organizationId,
            normalizedName,
            normalizedEmail,
            balanceCents,
            delinquentFlag,
            createdAt.ToUniversalTime());
    }

    public void Update(string name, string email)
    {
        Name = NormalizeName(name);
        Email = NormalizeEmail(email);
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalizedName = name.Trim();

        if (normalizedName.Length > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(name), "Name cannot exceed 200 characters.");
        }

        return normalizedName;
    }

    private static string NormalizeEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var atIndex = normalizedEmail.IndexOf('@');

        if (normalizedEmail.Length > 320 ||
            atIndex <= 0 ||
            atIndex != normalizedEmail.LastIndexOf('@'))
        {
            throw InvalidEmail(email);
        }

        var localPart = normalizedEmail[..atIndex];
        var domain = normalizedEmail[(atIndex + 1)..];
        if (!IsValidLocalPart(localPart) || !IsValidDomain(domain))
        {
            throw InvalidEmail(email);
        }

        return normalizedEmail;
    }

    private static bool IsValidLocalPart(string localPart) =>
        localPart.Length <= 64 &&
        !localPart.StartsWith('.') &&
        !localPart.EndsWith('.') &&
        !localPart.Contains("..", StringComparison.Ordinal) &&
        localPart.All(IsAllowedLocalCharacter);

    private static bool IsAllowedLocalCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) ||
        "!#$%&'*+-/=?^_`{|}~.".Contains(character);

    private static bool IsValidDomain(string domain)
    {
        if (domain.Length is < 3 or > 255 ||
            domain.StartsWith('.') ||
            domain.EndsWith('.') ||
            domain.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var labels = domain.Split('.');
        return labels.Length >= 2 &&
            labels.All(label =>
                label.Length is > 0 and <= 63 &&
                char.IsAsciiLetterOrDigit(label[0]) &&
                char.IsAsciiLetterOrDigit(label[^1]) &&
                label.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')) &&
            labels[^1].All(char.IsAsciiLetter);
    }

    private static ArgumentException InvalidEmail(string email) =>
        new("Email must be a valid email address.", nameof(email));
}
