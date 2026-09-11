using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BillingPlatform.Webhooks.Infrastructure.Persistence;

public sealed class WebhooksDbContextFactory : IDesignTimeDbContextFactory<WebhooksDbContext>
{
    public WebhooksDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__BillingPlatform")
            ?? "Host=localhost;Database=billing_platform_design;Username=billing;Password=design-time-only";
        var options = new DbContextOptionsBuilder<WebhooksDbContext>()
            .UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__ef_migrations_history", "webhooks"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new WebhooksDbContext(options);
    }
}
