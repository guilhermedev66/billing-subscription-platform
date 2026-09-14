using System.Security.Claims;
using BillingPlatform.Reporting.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BillingPlatform.Reporting.Api;

public static class ReportingEndpoints
{
    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reporting")
            .WithTags("Reporting")
            .RequireAuthorization();
        group.MapGet("/summary", CurrentAsync);
        group.MapGet("/waterfall", WaterfallAsync);
        group.MapGet("/events/{sourceEventId:guid}", GetEventAsync);
        group.MapPost("/rebuild", RebuildAsync).RequireAuthorization("simulation-operator");
        return endpoints;
    }

    private static async Task<IResult> CurrentAsync(
        ClaimsPrincipal principal,
        IReportingService service,
        CancellationToken cancellationToken) =>
        TryOrganization(principal, out var organizationId)
            ? Results.Ok(await service.CurrentAsync(organizationId, cancellationToken))
            : Results.Forbid();

    private static async Task<IResult> WaterfallAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        ClaimsPrincipal principal,
        IReportingService service,
        CancellationToken cancellationToken)
    {
        if (!TryOrganization(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        if (to <= from)
        {
            return Results.BadRequest(new { error = "to must be after from" });
        }

        return Results.Ok(await service.WaterfallAsync(
            organizationId, from.ToUniversalTime(), to.ToUniversalTime(), cancellationToken));
    }

    private static async Task<IResult> GetEventAsync(
        Guid sourceEventId,
        ClaimsPrincipal principal,
        IReportingService service,
        CancellationToken cancellationToken)
    {
        if (!TryOrganization(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        var result = await service.GetEventAsync(organizationId, sourceEventId, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> RebuildAsync(
        ClaimsPrincipal principal,
        IReportingService service,
        CancellationToken cancellationToken)
    {
        if (!TryOrganization(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        await service.RebuildAsync(organizationId, cancellationToken);
        return Results.NoContent();
    }

    private static bool TryOrganization(ClaimsPrincipal principal, out Guid organizationId) =>
        Guid.TryParse(principal.FindFirstValue("org_id"), out organizationId);
}
