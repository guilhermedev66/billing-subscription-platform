using BillingPlatform.Subscriptions.Application;
using BillingPlatform.Subscriptions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BillingPlatform.Subscriptions.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSubscriptionsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BillingPlatform")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:BillingPlatform is required.");

        services.AddDbContext<SubscriptionsDbContext>(options =>
            options
                .UseNpgsql(
                    connectionString,
                    postgres => postgres.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        "subscriptions"))
                .UseSnakeCaseNamingConvention());

        services.AddSingleton<IProrationCalculator, ProrationCalculator>();
        services.AddScoped<SubscriptionService>();
        services.AddScoped<ISubscriptionService>(provider =>
            provider.GetRequiredService<SubscriptionService>());
        services.AddScoped<ISubscriptionPaymentStateService>(provider =>
            provider.GetRequiredService<SubscriptionService>());

        return services;
    }
}
