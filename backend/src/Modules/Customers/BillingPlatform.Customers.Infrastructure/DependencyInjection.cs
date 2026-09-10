using BillingPlatform.Customers.Application;
using BillingPlatform.Customers.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BillingPlatform.Customers.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCustomersModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BillingPlatform")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:BillingPlatform is required.");

        services.AddDbContext<CustomersDbContext>(options =>
            options
                .UseNpgsql(
                    connectionString,
                    postgres => postgres.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        "customers"))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<CustomerService>();
        services.AddScoped<ICustomerService>(provider =>
            provider.GetRequiredService<CustomerService>());

        return services;
    }
}
