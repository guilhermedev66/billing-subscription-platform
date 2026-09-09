namespace BillingPlatform.Organizations.Domain;

public sealed class OrganizationMembership
{
    private OrganizationMembership()
    {
    }

    private OrganizationMembership(Guid organizationId, Guid userId, DateTimeOffset createdAt)
    {
        OrganizationId = organizationId;
        UserId = userId;
        CreatedAt = createdAt;
    }

    public Guid OrganizationId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static OrganizationMembership Create(
        Guid organizationId,
        Guid userId,
        DateTimeOffset createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(organizationId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);

        return new OrganizationMembership(organizationId, userId, createdAt.ToUniversalTime());
    }
}
