using System.Security.Cryptography;
using BillingPlatform.Organizations.Application;
using BillingPlatform.Organizations.Domain;
using BillingPlatform.Organizations.Infrastructure.Persistence;
using BillingPlatform.SimulationClock.Application;
using Microsoft.EntityFrameworkCore;

namespace BillingPlatform.Organizations.Infrastructure;

internal sealed class OrganizationService(
    OrganizationsDbContext dbContext,
    IVirtualClock clock) : IOrganizationService, IOrganizationMembershipReader, IOrganizationBillingReader
{
    public async Task<OrganizationSummary> CreateAsync(
        Guid userId,
        CreateOrganizationCommand command,
        CancellationToken cancellationToken = default)
    {
        var now = clock.Now;
        var organization = Organization.Create(
            Guid.NewGuid(),
            command.Name,
            command.DefaultCurrency,
            command.InvoicePrefix,
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)),
            command.SimulationModeEnabled,
            now);
        var membership = OrganizationMembership.Create(organization.Id, userId, now);

        dbContext.Organizations.Add(organization);
        dbContext.Memberships.Add(membership);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToSummary(organization);
    }

    public async Task<OrganizationSummary?> GetAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Organizations
            .AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Where(organization => dbContext.Memberships.Any(membership =>
                membership.OrganizationId == organization.Id && membership.UserId == userId))
            .Select(organization => new OrganizationSummary(
                organization.Id,
                organization.Name,
                organization.DefaultCurrency,
                organization.InvoicePrefix,
                organization.SimulationModeEnabled,
                organization.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid?> FindPrimaryOrganizationIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Memberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId)
            .OrderBy(membership => membership.CreatedAt)
            .ThenBy(membership => membership.OrganizationId)
            .Select(membership => (Guid?)membership.OrganizationId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<OrganizationBillingDetails?> GetBillingDetailsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Organizations
            .AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new OrganizationBillingDetails(
                organization.Id,
                organization.InvoicePrefix,
                organization.DefaultCurrency))
            .SingleOrDefaultAsync(cancellationToken);

    private static OrganizationSummary ToSummary(Organization organization) =>
        new(
            organization.Id,
            organization.Name,
            organization.DefaultCurrency,
            organization.InvoicePrefix,
            organization.SimulationModeEnabled,
            organization.CreatedAt);
}
