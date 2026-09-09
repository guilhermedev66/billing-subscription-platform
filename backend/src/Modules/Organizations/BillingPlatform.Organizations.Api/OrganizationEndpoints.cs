using System.Security.Claims;
using BillingPlatform.Organizations.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BillingPlatform.Organizations.Api;

public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/organizations")
            .WithTags("Organizations")
            .RequireAuthorization();

        group.MapPost("/", CreateAsync);
        group.MapGet("/current", GetCurrentAsync);
        group.MapGet("/{organizationId:guid}", GetByIdAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateOrganizationRequest request,
        ClaimsPrincipal principal,
        IOrganizationService organizationService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var organization = await organizationService.CreateAsync(
                userId,
                new CreateOrganizationCommand(
                    request.Name,
                    request.DefaultCurrency,
                    request.InvoicePrefix,
                    request.SimulationModeEnabled),
                cancellationToken);

            return Results.Created($"/api/organizations/{organization.Id}", organization);
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["organization"] = [exception.Message]
            });
        }
    }

    private static async Task<IResult> GetCurrentAsync(
        ClaimsPrincipal principal,
        IOrganizationService organizationService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId) ||
            !TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        return await GetAuthorizedOrganizationAsync(
            userId,
            organizationId,
            organizationService,
            cancellationToken);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid organizationId,
        ClaimsPrincipal principal,
        IOrganizationService organizationService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId) ||
            !TryGetOrganizationId(principal, out var claimedOrganizationId) ||
            organizationId != claimedOrganizationId)
        {
            return Results.Forbid();
        }

        return await GetAuthorizedOrganizationAsync(
            userId,
            organizationId,
            organizationService,
            cancellationToken);
    }

    private static async Task<IResult> GetAuthorizedOrganizationAsync(
        Guid userId,
        Guid organizationId,
        IOrganizationService organizationService,
        CancellationToken cancellationToken)
    {
        var organization = await organizationService.GetAsync(
            userId,
            organizationId,
            cancellationToken);

        return organization is null ? Results.NotFound() : Results.Ok(organization);
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue("sub"), out userId);

    private static bool TryGetOrganizationId(
        ClaimsPrincipal principal,
        out Guid organizationId) =>
        Guid.TryParse(principal.FindFirstValue("org_id"), out organizationId);

    private sealed record CreateOrganizationRequest(
        string Name,
        string DefaultCurrency = "USD",
        string InvoicePrefix = "INV",
        bool SimulationModeEnabled = true);
}
