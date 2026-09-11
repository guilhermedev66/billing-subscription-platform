using System.Security.Claims;
using System.Transactions;
using BillingPlatform.Identity.Application;
using BillingPlatform.Organizations.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BillingPlatform.Identity.Api;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth").WithTags("Identity");

        group.MapPost("/register", RegisterAsync).AllowAnonymous();
        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IdentityRateLimiting.LoginPolicy);
        group.MapGet("/me", GetCurrentUser).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        IIdentityService identityService,
        IOrganizationService organizationService,
        ITokenService tokenService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OrganizationName) ||
            request.OrganizationName.Trim().Length > 200)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["organizationName"] = ["Organization name is required and cannot exceed 200 characters."]
            });
        }

        using var transaction = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions
            {
                IsolationLevel = IsolationLevel.ReadCommitted,
                Timeout = TransactionManager.DefaultTimeout
            },
            TransactionScopeAsyncFlowOption.Enabled);

        var result = await identityService.RegisterAsync(
            new RegisterIdentityCommand(request.Email, request.Password),
            cancellationToken);

        if (!result.Succeeded)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["identity"] = result.Errors.ToArray()
            });
        }

        var user = result.User!;
        var organization = await organizationService.CreateAsync(
            user.Id,
            new CreateOrganizationCommand(request.OrganizationName, "USD", "INV", true),
            cancellationToken);
        var accessToken = tokenService.Create(user, organization.Id, organization.SimulationModeEnabled);
        transaction.Complete();

        return Results.Created(
            "/api/auth/me",
            CreateSession(accessToken.Token, user, organization));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IIdentityService identityService,
        IOrganizationMembershipReader membershipReader,
        IOrganizationService organizationService,
        ITokenService tokenService,
        CancellationToken cancellationToken)
    {
        var user = await identityService.AuthenticateAsync(
            new LoginIdentityCommand(request.Email, request.Password),
            cancellationToken);
        if (user is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid credentials");
        }

        var organizationId = await membershipReader.FindPrimaryOrganizationIdAsync(
            user.Id,
            cancellationToken);
        if (organizationId is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Organization membership required");
        }

        var organization = await organizationService.GetAsync(
            user.Id,
            organizationId.Value,
            cancellationToken);
        if (organization is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Organization membership required");
        }

        var accessToken = tokenService.Create(user, organization.Id, organization.SimulationModeEnabled);

        return Results.Ok(CreateSession(accessToken.Token, user, organization));
    }

    private static IResult GetCurrentUser(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue("sub");
        var email = principal.FindFirstValue("email");
        var organizationId = principal.FindFirstValue("org_id");

        return Results.Ok(new { userId, email, organizationId });
    }

    private static AuthSessionResponse CreateSession(
        string token,
        IdentityUserInfo user,
        OrganizationSummary organization) =>
        new(
            token,
            new AuthUserResponse(user.Id, user.Email, organization.Id, organization.Name));

    private sealed record RegisterRequest(
        string OrganizationName,
        string Email,
        string Password);

    private sealed record LoginRequest(string Email, string Password);

    private sealed record AuthSessionResponse(string Token, AuthUserResponse User);

    private sealed record AuthUserResponse(
        Guid Id,
        string Email,
        Guid OrganizationId,
        string OrganizationName);
}
