using BillingPlatform.Organizations.Application;
using BillingPlatform.Organizations.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BillingPlatform.Organizations.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOrganizationsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BillingPlatform")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:BillingPlatform is required.");

        services.AddDbContext<OrganizationsDbContext>(options =>
            options
                .UseNpgsql(
                    connectionString,
                    postgres => postgres.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        "organizations"))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<OrganizationService>();
        services.AddScoped<IOrganizationService>(provider =>
            provider.GetRequiredService<OrganizationService>());
        services.AddScoped<IOrganizationMembershipReader>(provider =>
            provider.GetRequiredService<OrganizationService>());

        return services;
    }
}
