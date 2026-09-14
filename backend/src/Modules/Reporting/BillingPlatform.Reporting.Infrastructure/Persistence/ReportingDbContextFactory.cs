using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BillingPlatform.Reporting.Infrastructure.Persistence;

public sealed class ReportingDbContextFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__BillingPlatform")
            ?? "Host=localhost;Database=billingplatform;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__ef_migrations_history", "reporting"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ReportingDbContext(options);
    }
}
