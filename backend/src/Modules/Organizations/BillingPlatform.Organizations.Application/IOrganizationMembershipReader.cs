namespace BillingPlatform.Organizations.Application;

public interface IOrganizationMembershipReader
{
    // M1 uses the earliest membership as primary until explicit multi-organization selection exists.
    Task<Guid?> FindPrimaryOrganizationIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
