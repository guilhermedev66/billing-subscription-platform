using System.Security.Claims;
using BillingPlatform.Customers.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BillingPlatform.Customers.Api;

public static class CustomersEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/customers")
            .WithTags("Customers")
            .RequireAuthorization();

        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{customerId:guid}", GetByIdAsync);
        group.MapPut("/{customerId:guid}", UpdateAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateCustomerRequest request,
        ClaimsPrincipal principal,
        ICustomerService customerService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        try
        {
            var customer = await customerService.CreateAsync(
                organizationId,
                new CreateCustomerCommand(
                    request.Name,
                    request.Email,
                    0,
                    false),
                cancellationToken);

            return Results.Created($"/api/customers/{customer.Id}", customer);
        }
        catch (DuplicateCustomerEmailException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A customer with this email already exists.");
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["customer"] = [exception.Message]
            });
        }
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal,
        ICustomerService customerService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        return Results.Ok(await customerService.ListAsync(organizationId, cancellationToken));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid customerId,
        ClaimsPrincipal principal,
        ICustomerService customerService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        var customer = await customerService.GetAsync(
            organizationId,
            customerId,
            cancellationToken);
        if (customer is not null)
        {
            return Results.Ok(customer);
        }

        return Results.NotFound();
    }

    private static async Task<IResult> UpdateAsync(
        Guid customerId,
        UpdateCustomerRequest request,
        ClaimsPrincipal principal,
        ICustomerService customerService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        try
        {
            var customer = await customerService.UpdateAsync(
                organizationId,
                customerId,
                new UpdateCustomerCommand(
                    request.Name,
                    request.Email),
                cancellationToken);
            if (customer is not null)
            {
                return Results.Ok(customer);
            }

            return Results.NotFound();
        }
        catch (DuplicateCustomerEmailException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A customer with this email already exists.");
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["customer"] = [exception.Message]
            });
        }
    }

    private static bool TryGetOrganizationId(
        ClaimsPrincipal principal,
        out Guid organizationId) =>
        Guid.TryParse(principal.FindFirstValue("org_id"), out organizationId);

    private sealed record CreateCustomerRequest(string Name, string Email);

    private sealed record UpdateCustomerRequest(string Name, string Email);
}
