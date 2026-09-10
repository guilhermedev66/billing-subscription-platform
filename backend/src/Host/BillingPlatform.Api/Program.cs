using BillingPlatform.Api;
using BillingPlatform.Catalog.Api;
using BillingPlatform.Catalog.Infrastructure;
using BillingPlatform.Customers.Api;
using BillingPlatform.Customers.Infrastructure;
using BillingPlatform.Identity.Api;
using BillingPlatform.Identity.Infrastructure;
using BillingPlatform.Organizations.Api;
using BillingPlatform.Organizations.Infrastructure;
using BillingPlatform.SimulationClock.Infrastructure;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, loggerConfiguration) =>
        loggerConfiguration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext());

    builder.Services.AddProblemDetails();
    builder.Services.AddOpenApi();
    builder.Services.AddSimulationClock();
    builder.Services.AddIdentityRateLimiting();
    builder.Services.AddIdentityModule(builder.Configuration);
    builder.Services.AddOrganizationsModule(builder.Configuration);
    builder.Services.AddCustomersModule(builder.Configuration);
    builder.Services.AddCatalogModule(builder.Configuration);
    builder.Services.AddPlatformObservability(builder.Configuration, builder.Environment);
    builder.Services.AddPlatformHealthChecks(builder.Configuration);
    builder.Services.AddFrontendCors(builder.Configuration);

    var app = builder.Build();

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
    app.MapOpenApi();
    app.MapPlatformHealthChecks();
    app.MapIdentityEndpoints();
    app.MapOrganizationEndpoints();
    app.MapCustomerEndpoints();
    app.MapCatalogEndpoints();

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
