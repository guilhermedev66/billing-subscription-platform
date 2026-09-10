using BillingPlatform.Customers.Application;
using BillingPlatform.Customers.Domain;
using BillingPlatform.Customers.Infrastructure.Persistence;
using BillingPlatform.SimulationClock.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BillingPlatform.Customers.Infrastructure;

internal sealed class CustomerService(
    CustomersDbContext dbContext,
    IVirtualClock clock) : ICustomerService
{
    public async Task<CustomerSummary> CreateAsync(
        Guid organizationId,
        CreateCustomerCommand command,
        CancellationToken cancellationToken = default)
    {
        var customer = Customer.Create(
            Guid.NewGuid(),
            organizationId,
            command.Name,
            command.Email,
            command.BalanceCents,
            command.DelinquentFlag,
            clock.Now);

        dbContext.Customers.Add(customer);
        await SaveChangesAsync(cancellationToken);
        return ToSummary(customer);
    }

    public async Task<IReadOnlyList<CustomerSummary>> ListAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Customers
            .AsNoTracking()
            .Where(customer => customer.OrganizationId == organizationId)
            .OrderBy(customer => customer.Name)
            .ThenBy(customer => customer.Id)
            .Select(customer => new CustomerSummary(
                customer.Id,
                customer.OrganizationId,
                customer.Name,
                customer.Email,
                customer.BalanceCents,
                customer.DelinquentFlag,
                customer.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<CustomerSummary?> GetAsync(
        Guid organizationId,
        Guid customerId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Customers
            .AsNoTracking()
            .Where(customer => customer.OrganizationId == organizationId)
            .Where(customer => customer.Id == customerId)
            .Select(customer => new CustomerSummary(
                customer.Id,
                customer.OrganizationId,
                customer.Name,
                customer.Email,
                customer.BalanceCents,
                customer.DelinquentFlag,
                customer.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<CustomerSummary?> UpdateAsync(
        Guid organizationId,
        Guid customerId,
        UpdateCustomerCommand command,
        CancellationToken cancellationToken = default)
    {
        var customer = await dbContext.Customers
            .Where(customer => customer.OrganizationId == organizationId)
            .Where(customer => customer.Id == customerId)
            .SingleOrDefaultAsync(cancellationToken);

        if (customer is null)
        {
            return null;
        }

        customer.Update(
            command.Name,
            command.Email);
        await SaveChangesAsync(cancellationToken);
        return ToSummary(customer);
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateEmailViolation(exception))
        {
            throw new DuplicateCustomerEmailException();
        }
    }

    private static bool IsDuplicateEmailViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException &&
        postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
        postgresException.ConstraintName == "ix_customers_organization_id_email";

    private static CustomerSummary ToSummary(Customer customer) =>
        new(
            customer.Id,
            customer.OrganizationId,
            customer.Name,
            customer.Email,
            customer.BalanceCents,
            customer.DelinquentFlag,
            customer.CreatedAt);
}
