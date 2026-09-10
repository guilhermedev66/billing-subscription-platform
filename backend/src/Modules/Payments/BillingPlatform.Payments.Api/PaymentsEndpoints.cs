using System.Security.Claims;
using BillingPlatform.Payments.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BillingPlatform.Payments.Api;

public static class PaymentsEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/payments").WithTags("Payments").RequireAuthorization();
        group.MapGet("/invoices/{invoiceId:guid}/attempts", ListAttemptsAsync);
        group.MapPost("/invoices/{invoiceId:guid}/attempt", AttemptAsync);
        group.MapPost("/dunning-sweep", SweepAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAttemptsAsync(
        Guid invoiceId,
        ClaimsPrincipal principal,
        IPaymentService paymentService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        var attempts = await paymentService.ListAttemptsAsync(
            organizationId,
            invoiceId,
            cancellationToken);
        return attempts is null ? Results.NotFound() : Results.Ok(attempts);
    }

    private static async Task<IResult> AttemptAsync(
        Guid invoiceId,
        PaymentRequest request,
        ClaimsPrincipal principal,
        HttpRequest httpRequest,
        IPaymentTransactionCoordinator paymentCoordinator,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        if (!TryGetIdempotencyKey(httpRequest, out var idempotencyKey))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The Idempotency-Key header is required for payment mutations.");
        }

        try
        {
            var result = await paymentCoordinator.AttemptAsync(
                organizationId,
                invoiceId,
                request.CardNumber,
                idempotencyKey,
                cancellationToken);
            if (result is null)
            {
                return Results.NotFound();
            }

            return result.Replayed
                ? new StoredJsonResult(result.ResponseBody)
                : Results.Ok(new
                {
                    attempt = result.Attempt,
                    invoice = result.Invoice
                });
        }
        catch (PaymentIdempotencyConflictException exception)
        {
            return Conflict(exception);
        }
        catch (PaymentConcurrencyException exception)
        {
            return Conflict(exception);
        }
        catch (PaymentStateConflictException exception)
        {
            return Conflict(exception);
        }
        catch (PaymentProcessingException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["payment"] = [exception.Message]
            });
        }
    }

    private static async Task<IResult> SweepAsync(
        ClaimsPrincipal principal,
        HttpRequest httpRequest,
        IPaymentTransactionCoordinator paymentCoordinator,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        if (!TryGetIdempotencyKey(httpRequest, out var idempotencyKey))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The Idempotency-Key header is required for payment mutations.");
        }

        try
        {
            var result = await paymentCoordinator.SweepDunningAsync(
                organizationId, idempotencyKey, cancellationToken);
            return result.Replayed
                ? new StoredJsonResult(result.ResponseBody)
                : Results.Ok(result.Value);
        }
        catch (PaymentConcurrencyException exception)
        {
            return Conflict(exception);
        }
    }

    private static IResult Conflict(Exception exception) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: exception.Message);

    private static bool TryGetIdempotencyKey(HttpRequest request, out string key)
    {
        if (request.Headers.TryGetValue("Idempotency-Key", out var values) &&
            values.Count == 1 && !string.IsNullOrWhiteSpace(values[0]))
        {
            key = values[0]!;
            return true;
        }

        key = string.Empty;
        return false;
    }

    private static bool TryGetOrganizationId(ClaimsPrincipal principal, out Guid organizationId) =>
        Guid.TryParse(principal.FindFirstValue("org_id"), out organizationId);

    private sealed class StoredJsonResult(string responseBody) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "application/json; charset=utf-8";
            await httpContext.Response.WriteAsync(responseBody);
        }
    }
}
