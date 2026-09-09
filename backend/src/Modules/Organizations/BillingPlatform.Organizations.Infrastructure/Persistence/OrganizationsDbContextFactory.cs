using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BillingPlatform.Organizations.Infrastructure.Persistence;

public sealed class OrganizationsDbContextFactory
    : IDesignTimeDbContextFactory<OrganizationsDbContext>
{
    public OrganizationsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__BillingPlatform")
            ?? "Host=localhost;Port=5432;Database=billing_platform;Username=billing;Password=billing_dev";

        var options = new DbContextOptionsBuilder<OrganizationsDbContext>()
            .UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsHistoryTable(
                    "__ef_migrations_history",
                    "organizations"))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new OrganizationsDbContext(options);
    }
}
