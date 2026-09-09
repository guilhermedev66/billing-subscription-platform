namespace BillingPlatform.Organizations.Application;

public sealed record CreateOrganizationCommand(
    string Name,
    string DefaultCurrency,
    string InvoicePrefix,
    bool SimulationModeEnabled);

public sealed record OrganizationSummary(
    Guid Id,
    string Name,
    string DefaultCurrency,
    string InvoicePrefix,
    bool SimulationModeEnabled,
    DateTimeOffset CreatedAt);

public interface IOrganizationService
{
    Task<OrganizationSummary> CreateAsync(
        Guid userId,
        CreateOrganizationCommand command,
        CancellationToken cancellationToken = default);

    Task<OrganizationSummary?> GetAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
