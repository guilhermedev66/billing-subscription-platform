namespace BillingPlatform.Catalog.Domain;

public sealed class Product
{
    private Product()
    {
    }

    private Product(
        Guid id,
        Guid organizationId,
        string name,
        string description,
        bool active)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
        Description = description;
        Active = active;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public bool Active { get; private set; }

    public static Product Create(
        Guid id,
        Guid organizationId,
        string name,
        string description,
        bool active)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(organizationId, Guid.Empty);

        return new Product(
            id,
            organizationId,
            NormalizeName(name),
            NormalizeDescription(description),
            active);
    }

    public void Update(string name, string description, bool active)
    {
        Name = NormalizeName(name);
        Description = NormalizeDescription(description);
        Active = active;
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

    private static string NormalizeDescription(string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        var normalizedDescription = description.Trim();

        if (normalizedDescription.Length > 2000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(description),
                "Description cannot exceed 2000 characters.");
        }

        return normalizedDescription;
    }
}
