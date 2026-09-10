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

public sealed record OrganizationBillingDetails(
    Guid Id,
    string InvoicePrefix,
    string DefaultCurrency);

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

public interface IOrganizationBillingReader
{
    Task<OrganizationBillingDetails?> GetBillingDetailsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
