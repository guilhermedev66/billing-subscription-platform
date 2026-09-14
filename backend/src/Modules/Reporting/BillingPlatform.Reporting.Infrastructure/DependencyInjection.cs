using BillingPlatform.Catalog.Application;
using BillingPlatform.Reporting.Application;
using BillingPlatform.Reporting.Infrastructure.Persistence;
using BillingPlatform.Subscriptions.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BillingPlatform.Reporting.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddReportingModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BillingPlatform")
            ?? throw new InvalidOperationException("ConnectionStrings:BillingPlatform is required.");
        services.AddDbContext<ReportingDbContext>(options =>
            options.UseNpgsql(connectionString, postgres =>
                    postgres.MigrationsHistoryTable("__ef_migrations_history", "reporting"))
                .UseSnakeCaseNamingConvention());
        services.AddScoped<ReportingService>();
        services.AddScoped<IReportingService>(provider => provider.GetRequiredService<ReportingService>());
        services.AddScoped<ISubscriptionRevenueEventWriter>(provider => provider.GetRequiredService<ReportingService>());
        services.AddScoped<IPriceMutationEventWriter>(provider => provider.GetRequiredService<ReportingService>());
        return services;
    }
}
