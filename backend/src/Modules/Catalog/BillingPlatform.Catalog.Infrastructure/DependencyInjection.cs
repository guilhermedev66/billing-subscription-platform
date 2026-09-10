using BillingPlatform.Catalog.Application;
using BillingPlatform.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BillingPlatform.Catalog.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCatalogModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BillingPlatform")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:BillingPlatform is required.");

        services.AddDbContext<CatalogDbContext>(options =>
            options
                .UseNpgsql(
                    connectionString,
                    postgres => postgres.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        "catalog"))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<ProductService>();
        services.AddScoped<IProductService>(provider =>
            provider.GetRequiredService<ProductService>());
        services.AddScoped<PriceService>();
        services.AddScoped<IPriceService>(provider =>
            provider.GetRequiredService<PriceService>());

        return services;
    }
}
