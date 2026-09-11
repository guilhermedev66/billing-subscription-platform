using BillingPlatform.Webhooks.Application;
using BillingPlatform.Webhooks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BillingPlatform.Webhooks.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWebhooksModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BillingPlatform")
            ?? throw new InvalidOperationException("ConnectionStrings:BillingPlatform is required.");
        services.AddDbContext<WebhooksDbContext>(options =>
            options.UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__ef_migrations_history", "webhooks"))
                .UseSnakeCaseNamingConvention());
        services.AddSingleton<IWebhookDestinationResolver, WebhookDestinationResolver>();
        services.AddHttpClient("webhooks")
            .ConfigurePrimaryHttpMessageHandler(provider =>
                WebhookHttpMessageHandlerFactory.Create(
                    provider.GetRequiredService<IWebhookDestinationResolver>(),
                    provider.GetRequiredService<IHostEnvironment>()));
        services.AddScoped<IWebhookService, WebhookService>();
        services.AddScoped<IWebhookEventWriter>(provider =>
            provider.GetRequiredService<IWebhookService>());
        services.AddHostedService<WebhookDispatcherWorker>();
        return services;
    }
}
