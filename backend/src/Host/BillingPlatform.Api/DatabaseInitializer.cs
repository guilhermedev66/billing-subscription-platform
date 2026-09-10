using BillingPlatform.Catalog.Infrastructure.Persistence;
using BillingPlatform.Customers.Infrastructure.Persistence;
using BillingPlatform.Identity.Infrastructure.Persistence;
using BillingPlatform.Organizations.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingPlatform.Api;

internal static class DatabaseInitializer
{
    public static async Task ApplyMigrationsAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();

        var identityDbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await identityDbContext.Database.MigrateAsync();

        var organizationsDbContext =
            scope.ServiceProvider.GetRequiredService<OrganizationsDbContext>();
        await organizationsDbContext.Database.MigrateAsync();

        var customersDbContext =
            scope.ServiceProvider.GetRequiredService<CustomersDbContext>();
        await customersDbContext.Database.MigrateAsync();

        var catalogDbContext =
            scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await catalogDbContext.Database.MigrateAsync();
    }
}
