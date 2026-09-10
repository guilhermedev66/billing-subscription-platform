namespace BillingPlatform.Customers.Application;

public sealed class DuplicateCustomerEmailException : Exception
{
    public DuplicateCustomerEmailException()
        : base("A customer with this email already exists in the organization.")
    {
    }
}

public sealed record CreateCustomerCommand(
    string Name,
    string Email,
    long BalanceCents,
    bool DelinquentFlag);

public sealed record UpdateCustomerCommand(
    string Name,
    string Email);

public sealed record CustomerSummary(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string Email,
    long BalanceCents,
    bool DelinquentFlag,
    DateTimeOffset CreatedAt);

public interface ICustomerService
{
    Task<CustomerSummary> CreateAsync(
        Guid organizationId,
        CreateCustomerCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSummary>> ListAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<CustomerSummary?> GetAsync(
        Guid organizationId,
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task<CustomerSummary?> UpdateAsync(
        Guid organizationId,
        Guid customerId,
        UpdateCustomerCommand command,
        CancellationToken cancellationToken = default);
}
