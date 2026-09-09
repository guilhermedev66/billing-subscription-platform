namespace BillingPlatform.Organizations.Application;

public interface IOrganizationMembershipReader
{
    Task<Guid?> FindPrimaryOrganizationIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
