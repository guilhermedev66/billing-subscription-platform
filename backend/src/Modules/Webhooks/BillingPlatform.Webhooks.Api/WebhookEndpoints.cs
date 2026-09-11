using System.Security.Claims;
using BillingPlatform.Webhooks.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BillingPlatform.Webhooks.Api;

public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/webhooks").WithTags("Webhooks").RequireAuthorization();
        group.MapPost("/endpoints", RegisterEndpointAsync);
        group.MapPost("/", RegisterEndpointAsync);
        group.MapGet("/endpoints", ListEndpointsAsync);
        group.MapGet("/events", ListEventsAsync);
        group.MapGet("/events/{eventId:guid}/deliveries", ListDeliveriesAsync);
        group.MapPost("/dispatch", DispatchAsync);
        group.MapPost("/{endpointId:guid}/inbound", IngestAsync);
        group.MapPost("/{endpointId:guid}/events", IngestAsync);
        group.MapPost("/inbound/{endpointId:guid}", IngestAsync);
        return endpoints;
    }

    private static async Task<IResult> RegisterEndpointAsync(
        RegisterWebhookEndpointRequest request,
        ClaimsPrincipal principal,
        HttpRequest httpRequest,
        IWebhookService webhookService,
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
                title: "The Idempotency-Key header is required for webhook mutations.");
        }

        try
        {
            var result = await webhookService.RegisterEndpointAsync(
                organizationId,
                new RegisterWebhookEndpointCommand(request.Url, request.Secret, request.EventTypes),
                idempotencyKey,
                cancellationToken);
            return result.Replayed
                ? new StoredJsonResult(result.ResponseBody, result.StatusCode, result.Location)
                : Results.Created(result.Location!, result.Value);
        }
        catch (WebhookIdempotencyConflictException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["webhook"] = [exception.Message] });
        }
    }

    private static async Task<IResult> ListEndpointsAsync(
        ClaimsPrincipal principal,
        IWebhookService webhookService,
        CancellationToken cancellationToken) =>
        !TryGetOrganizationId(principal, out var organizationId)
            ? Results.Forbid()
            : Results.Ok(await webhookService.ListEndpointsAsync(organizationId, cancellationToken));

    private static async Task<IResult> ListEventsAsync(
        ClaimsPrincipal principal,
        IWebhookService webhookService,
        CancellationToken cancellationToken) =>
        !TryGetOrganizationId(principal, out var organizationId)
            ? Results.Forbid()
            : Results.Ok(await webhookService.ListEventsAsync(organizationId, cancellationToken));

    private static async Task<IResult> ListDeliveriesAsync(
        Guid eventId,
        ClaimsPrincipal principal,
        IWebhookService webhookService,
        CancellationToken cancellationToken) =>
        !TryGetOrganizationId(principal, out var organizationId)
            ? Results.Forbid()
            : await ListDeliveriesResultAsync(organizationId, eventId, webhookService, cancellationToken);

    private static async Task<IResult> ListDeliveriesResultAsync(
        Guid organizationId,
        Guid eventId,
        IWebhookService webhookService,
        CancellationToken cancellationToken)
    {
        var deliveries = await webhookService.ListDeliveriesAsync(organizationId, eventId, cancellationToken);
        return deliveries is null ? Results.NotFound() : Results.Ok(deliveries);
    }

    private static async Task<IResult> DispatchAsync(
        ClaimsPrincipal principal,
        IWebhookService webhookService,
        CancellationToken cancellationToken) =>
        !TryGetOrganizationId(principal, out var organizationId)
            ? Results.Forbid()
            : Results.Ok(await webhookService.DispatchAsync(organizationId, cancellationToken));

    private static async Task<IResult> IngestAsync(
        Guid endpointId,
        ClaimsPrincipal principal,
        HttpRequest request,
        IWebhookService webhookService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        if (!request.Headers.TryGetValue("X-Signature", out var signature) || signature.Count != 1)
        {
            return Results.Unauthorized();
        }

        await using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        try
        {
            var result = await webhookService.IngestAsync(
                organizationId, endpointId, buffer.ToArray(), signature[0]!, cancellationToken);
            return Results.Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (WebhookSignatureException)
        {
            return Results.Unauthorized();
        }
    }

    private static bool TryGetOrganizationId(ClaimsPrincipal principal, out Guid organizationId) =>
        Guid.TryParse(principal.FindFirstValue("org_id"), out organizationId);

    private static bool TryGetIdempotencyKey(HttpRequest request, out string key)
    {
        if (request.Headers.TryGetValue("Idempotency-Key", out var values) &&
            values.Count == 1 &&
            !string.IsNullOrWhiteSpace(values[0]))
        {
            key = values[0]!;
            return true;
        }

        key = string.Empty;
        return false;
    }

    private sealed record RegisterWebhookEndpointRequest(
        string Url,
        string Secret,
        IReadOnlyList<string>? EventTypes);

    private sealed class StoredJsonResult(
        string responseBody,
        int statusCode,
        string? location) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.ContentType = "application/json; charset=utf-8";
            if (location is not null)
            {
                httpContext.Response.Headers.Location = location;
            }

            await httpContext.Response.WriteAsync(responseBody);
        }
    }
}
