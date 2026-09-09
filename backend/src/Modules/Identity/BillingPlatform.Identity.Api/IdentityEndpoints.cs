using System.Security.Claims;
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
        group.MapPost("/token", CreateTokenAsync).AllowAnonymous();
        group.MapGet("/me", GetCurrentUser).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        IIdentityService identityService,
        CancellationToken cancellationToken)
    {
        var result = await identityService.RegisterAsync(
            new RegisterIdentityCommand(request.Email, request.Password),
            cancellationToken);

        return result.Succeeded
            ? Results.Created(
                "/api/auth/me",
                new { result.User!.Id, result.User.Email })
            : Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["identity"] = result.Errors.ToArray()
            });
    }

    private static async Task<IResult> CreateTokenAsync(
        LoginRequest request,
        IIdentityService identityService,
        IOrganizationMembershipReader membershipReader,
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
        var accessToken = tokenService.Create(user, organizationId);

        return Results.Ok(new
        {
            accessToken = accessToken.Token,
            tokenType = "Bearer",
            expiresAt = accessToken.ExpiresAt,
            organizationId
        });
    }

    private static IResult GetCurrentUser(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue("sub");
        var email = principal.FindFirstValue("email");
        var organizationId = principal.FindFirstValue("org_id");

        return Results.Ok(new { userId, email, organizationId });
    }

    private sealed record RegisterRequest(string Email, string Password);

    private sealed record LoginRequest(string Email, string Password);
}
