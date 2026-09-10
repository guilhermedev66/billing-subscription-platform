using System.Security.Claims;
using BillingPlatform.Billing.Application;
using BillingPlatform.Billing.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BillingPlatform.Billing.Api;

public static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/invoices").WithTags("Billing").RequireAuthorization();
        group.MapGet("/", ListAsync);
        group.MapGet("/{invoiceId:guid}", GetByIdAsync);
        group.MapPost("/{invoiceId:guid}/void", VoidAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal,
        IInvoiceService invoiceService,
        CancellationToken cancellationToken)
    {
        return !TryGetOrganizationId(principal, out var organizationId)
            ? Results.Forbid()
            : Results.Ok(await invoiceService.ListAsync(organizationId, cancellationToken));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid invoiceId,
        ClaimsPrincipal principal,
        IInvoiceService invoiceService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        var invoice = await invoiceService.GetAsync(organizationId, invoiceId, cancellationToken);
        return invoice is null ? Results.NotFound() : Results.Ok(invoice);
    }

    private static async Task<IResult> VoidAsync(
        Guid invoiceId,
        ClaimsPrincipal principal,
        IInvoiceService invoiceService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        try
        {
            var invoice = await invoiceService.VoidAsync(
                organizationId,
                invoiceId,
                cancellationToken);
            return invoice is null ? Results.NotFound() : Results.Ok(invoice);
        }
        catch (InvoiceDomainException exception)
        {
            return Conflict(exception);
        }
        catch (InvoiceConcurrencyException exception)
        {
            return Conflict(exception);
        }
    }

    private static IResult Conflict(Exception exception) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: exception.Message);

    private static bool TryGetOrganizationId(ClaimsPrincipal principal, out Guid organizationId) =>
        Guid.TryParse(principal.FindFirstValue("org_id"), out organizationId);
}
