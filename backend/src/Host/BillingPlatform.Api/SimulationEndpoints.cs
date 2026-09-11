using System.Security.Claims;
using BillingPlatform.Billing.Application;
using BillingPlatform.Payments.Application;
using BillingPlatform.SimulationClock.Application;
using BillingPlatform.Subscriptions.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BillingPlatform.Api;

public static class SimulationEndpoints
{
    public static IEndpointRouteBuilder MapSimulationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/simulation")
            .WithTags("Simulation")
            .RequireAuthorization("simulation-operator");
        group.MapPost("/advance/{days:int}", AdvanceDaysAsync);
        group.MapPost("/advance", AdvanceAsync);
        group.MapPost("/advance-to-next-anchor", AdvanceToNextAnchorAsync);
        group.MapPost("/renewal-cron", RenewalCronAsync);
        group.MapPost("/trigger-renewal-cron", RenewalCronAsync);
        group.MapPost("/dunning-sweep", DunningSweepAsync);
        group.MapPost("/process-dunning-sweep", DunningSweepAsync);
        group.MapPost("/seed", SeedAsync);
        group.MapPost("/seed-demo", SeedAsync);
        group.MapGet("/clock", ClockAsync);
        return endpoints;
    }

    private static Task<IResult> AdvanceDaysAsync(
        int days,
        ClaimsPrincipal principal,
        IVirtualClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out _))
        {
            return Task.FromResult<IResult>(Results.Forbid());
        }

        // Time travel only moves the clock. Dunning remains an explicit, idempotent
        // operator action so a demo advance cannot silently consume a sweep key.
        if (days is not (1 or 7 or 30))
        {
            return Task.FromResult<IResult>(Results.ValidationProblem(
                new Dictionary<string, string[]> { ["days"] = ["Only 1, 7, or 30 day advances are supported."] }));
        }

        clock.FastForward(TimeSpan.FromDays(days));
        return Task.FromResult<IResult>(Results.Ok(new { now = clock.Now, advancedDays = days }));
    }

    private static Task<IResult> AdvanceAsync(
        AdvanceRequest request,
        ClaimsPrincipal principal,
        IVirtualClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out _))
        {
            return Task.FromResult<IResult>(Results.Forbid());
        }

        if (request.Days is not (1 or 7 or 30))
        {
            return Task.FromResult<IResult>(Results.ValidationProblem(
                new Dictionary<string, string[]> { ["days"] = ["Only 1, 7, or 30 day advances are supported."] }));
        }

        clock.FastForward(TimeSpan.FromDays(request.Days));
        return Task.FromResult<IResult>(Results.Ok(new { now = clock.Now, advancedDays = request.Days }));
    }

    private static async Task<IResult> AdvanceToNextAnchorAsync(
        ClaimsPrincipal principal,
        IVirtualClock clock,
        ISubscriptionService subscriptionService,
        IInvoiceService invoiceService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        var now = clock.Now;
        var subscriptionAnchor = (await subscriptionService.ListAsync(organizationId, cancellationToken))
            .Select(subscription => subscription.CurrentPeriodEnd)
            .Where(anchor => anchor > now)
            .DefaultIfEmpty()
            .Min();
        var dunningAnchor = (await invoiceService.ListAsync(organizationId, cancellationToken))
            .Select(invoice => invoice.NextRetryAt)
            .Where(anchor => anchor is not null && anchor > now)
            .Select(anchor => anchor!.Value)
            .DefaultIfEmpty()
            .Min();
        var next = new[] { subscriptionAnchor, dunningAnchor }
            .Where(anchor => anchor > now)
            .DefaultIfEmpty(now)
            .Min();
        if (next > now)
        {
            clock.FastForward(next - now);
        }

        return Results.Ok(new { now = clock.Now, advanced = next > now, anchor = next });
    }

    private static async Task<IResult> RenewalCronAsync(
        ClaimsPrincipal principal,
        RenewalCronService renewalCronService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        return Results.Ok(await renewalCronService.RunAsync(organizationId, cancellationToken));
    }

    private static async Task<IResult> DunningSweepAsync(
        ClaimsPrincipal principal,
        HttpRequest request,
        IPaymentTransactionCoordinator paymentCoordinator,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        if (!request.Headers.TryGetValue("Idempotency-Key", out var values) || values.Count != 1 ||
            string.IsNullOrWhiteSpace(values[0]))
        {
            return Results.Problem(statusCode: 400, title: "Idempotency-Key is required.");
        }

        var result = await paymentCoordinator.SweepDunningAsync(
            organizationId, values[0]!, cancellationToken);
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> SeedAsync(
        ClaimsPrincipal principal,
        SimulationSeeder seeder,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        return Results.Ok(await seeder.SeedAsync(organizationId, cancellationToken));
    }

    private static Task<IResult> ClockAsync(IVirtualClock clock) =>
        Task.FromResult<IResult>(Results.Ok(new { now = clock.Now }));

    private static bool TryGetOrganizationId(ClaimsPrincipal principal, out Guid organizationId) =>
        Guid.TryParse(principal.FindFirstValue("org_id"), out organizationId);

    private sealed record AdvanceRequest(int Days);
}
