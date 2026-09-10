using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BillingPlatform.Customers.Infrastructure.Persistence;

public sealed class CustomersDbContextFactory
    : IDesignTimeDbContextFactory<CustomersDbContext>
{
    public CustomersDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__BillingPlatform")
            ?? throw new InvalidOperationException(
                "ConnectionStrings__BillingPlatform is required for EF Core tooling.");

        var options = new DbContextOptionsBuilder<CustomersDbContext>()
            .UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsHistoryTable(
                    "__ef_migrations_history",
                    "customers"))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new CustomersDbContext(options);
    }
}
