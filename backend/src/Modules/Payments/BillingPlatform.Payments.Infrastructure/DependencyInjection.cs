using BillingPlatform.Payments.Application;
using BillingPlatform.Payments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BillingPlatform.Payments.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BillingPlatform")
            ?? throw new InvalidOperationException("ConnectionStrings:BillingPlatform is required.");
        services.AddDbContext<PaymentsDbContext>(options => options
            .UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "payments"))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<PaymentService>();
        services.AddScoped<IPaymentService>(provider => provider.GetRequiredService<PaymentService>());
        return services;
    }
}
