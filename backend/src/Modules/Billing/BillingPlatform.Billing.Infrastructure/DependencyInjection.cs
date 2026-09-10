using BillingPlatform.Billing.Application;
using BillingPlatform.Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BillingPlatform.Billing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBillingModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BillingPlatform")
            ?? throw new InvalidOperationException("ConnectionStrings:BillingPlatform is required.");
        services.AddDbContext<BillingDbContext>(options => options
            .UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "billing"))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<InvoiceService>();
        services.AddScoped<IInvoiceService>(provider => provider.GetRequiredService<InvoiceService>());
        return services;
    }
}
