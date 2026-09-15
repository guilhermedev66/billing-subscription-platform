using BillingPlatform.Api;
using BillingPlatform.Catalog.Api;
using BillingPlatform.Catalog.Infrastructure;
using BillingPlatform.Billing.Api;
using BillingPlatform.Billing.Infrastructure;
using BillingPlatform.Customers.Api;
using BillingPlatform.Customers.Infrastructure;
using BillingPlatform.Identity.Api;
using BillingPlatform.Identity.Infrastructure;
using BillingPlatform.Organizations.Api;
using BillingPlatform.Organizations.Infrastructure;
using BillingPlatform.Payments.Api;
using BillingPlatform.Payments.Application;
using BillingPlatform.Payments.Infrastructure;
using BillingPlatform.SimulationClock.Infrastructure;
using BillingPlatform.Subscriptions.Api;
using BillingPlatform.Subscriptions.Application;
using BillingPlatform.Subscriptions.Infrastructure;
using BillingPlatform.Webhooks.Api;
using BillingPlatform.Webhooks.Infrastructure;
using BillingPlatform.Reporting.Api;
using BillingPlatform.Reporting.Infrastructure;
using BillingPlatform.Catalog.Application;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
    PostgresConnectionString.Normalize(builder.Configuration);

    builder.Host.UseSerilog((context, services, loggerConfiguration) =>
        loggerConfiguration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext());

    builder.Services.AddProblemDetails();
    builder.Services.AddOpenApi();
    builder.Services.AddAuthorization(options =>
        options.AddPolicy("simulation-operator", policy =>
            policy.RequireClaim("simulation_operator", "true")));
    builder.Services.AddSimulationClock();
    builder.Services.AddIdentityRateLimiting();
    builder.Services.AddIdentityModule(builder.Configuration);
    builder.Services.AddOrganizationsModule(builder.Configuration);
    builder.Services.AddCustomersModule(builder.Configuration);
    builder.Services.AddCatalogModule(builder.Configuration);
    builder.Services.AddSubscriptionsModule(builder.Configuration);
    builder.Services.AddBillingModule(builder.Configuration);
    builder.Services.AddPaymentsModule(builder.Configuration);
    builder.Services.AddWebhooksModule(builder.Configuration);
    builder.Services.AddReportingModule(builder.Configuration);
    builder.Services.AddScoped<IPriceMutationCoordinator, PriceMutationCoordinator>();
    builder.Services.AddScoped<ISubscriptionPriceReader, CatalogSubscriptionPriceReader>();
    builder.Services.AddScoped<ISubscriptionChangeBillingOrchestrator, SubscriptionChangeBillingOrchestrator>();
    builder.Services.AddScoped<ISubscriptionMutationCoordinator, SubscriptionMutationCoordinator>();
    builder.Services.AddScoped<FinancialTransactionCoordinator>();
    builder.Services.AddScoped<IPaymentTransactionCoordinator, PaymentTransactionCoordinator>();
    builder.Services.AddScoped<RenewalCronService>();
    builder.Services.AddScoped<SimulationSeeder>();
    builder.Services.AddPlatformObservability(builder.Configuration, builder.Environment);
    builder.Services.AddPlatformHealthChecks(builder.Configuration);
    builder.Services.AddFrontendCors(builder.Configuration);
    // Render terminates TLS at its edge and forwards plain HTTP to this container, so without
    // this, Request.IsHttps (and therefore UseHsts()) is always false in production. Render's
    // proxy isn't a fixed, allowlistable IP the way a private network's would be, so
    // KnownNetworks/KnownProxies (which default to trusting only loopback) are cleared to trust
    // the proxy's X-Forwarded-* headers unconditionally. That is only safe because Render's edge
    // is this API's sole ingress; if it ever became reachable any other way, this would need a
    // real trusted-proxy allowlist instead.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

    var app = builder.Build();

    app.UseForwardedHeaders();

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    app.UseMiddleware<SecurityHeadersMiddleware>();
    app.UseExceptionHandler();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseCors("Frontend");
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapGet("/", () => Results.Ok(new
    {
        service = "BillingPlatform.Api",
        status = "running"
    }));
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.MapPlatformHealthChecks();
    app.MapIdentityEndpoints();
    app.MapOrganizationEndpoints();
    app.MapCustomerEndpoints();
    app.MapCatalogEndpoints();
    app.MapSubscriptionEndpoints();
    app.MapBillingEndpoints();
    app.MapPaymentEndpoints();
    app.MapWebhookEndpoints();
    app.MapReportingEndpoints();
    app.MapSimulationEndpoints();

    await DatabaseInitializer.ApplyMigrationsAsync(app.Services);
    await app.RunAsync();
}
catch (Exception exception)
{
    Log.Fatal(exception, "Billing Platform API terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program;
