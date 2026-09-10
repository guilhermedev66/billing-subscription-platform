using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BillingPlatform.Subscriptions.Infrastructure.Persistence;

public sealed class SubscriptionsDbContextFactory : IDesignTimeDbContextFactory<SubscriptionsDbContext>
{
    public SubscriptionsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__BillingPlatform")
            ?? "Host=localhost;Database=billing_platform_design;Username=billing;Password=design-time-only";

        var optionsBuilder = new DbContextOptionsBuilder<SubscriptionsDbContext>();
        optionsBuilder
            .UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsHistoryTable(
                    "__ef_migrations_history",
                    "subscriptions"))
            .UseSnakeCaseNamingConvention();

        return new SubscriptionsDbContext(optionsBuilder.Options);
    }
}
