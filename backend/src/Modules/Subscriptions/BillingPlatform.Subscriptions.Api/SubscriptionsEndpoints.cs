using System.Security.Claims;
using BillingPlatform.Subscriptions.Application;
using BillingPlatform.Subscriptions.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BillingPlatform.Subscriptions.Api;

public static class SubscriptionsEndpoints
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/subscriptions")
            .WithTags("Subscriptions")
            .RequireAuthorization();

        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{subscriptionId:guid}", GetByIdAsync);
        group.MapPost("/{subscriptionId:guid}/preview-proration", PreviewAsync);
        group.MapPost("/{subscriptionId:guid}/apply-change", ApplyAsync);
        group.MapPost("/{subscriptionId:guid}/cancel", CancelAsync);
        group.MapPost("/{subscriptionId:guid}/pause", PauseAsync);
        group.MapPost("/{subscriptionId:guid}/resume", ResumeAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal principal,
        HttpRequest httpRequest,
        ISubscriptionMutationCoordinator mutationCoordinator,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        if (!TryGetIdempotencyKey(httpRequest, out var idempotencyKey))
        {
            return MissingIdempotencyKey();
        }

        try
        {
            var result = await mutationCoordinator.CreateAsync(
                organizationId,
                new CreateSubscriptionCommand(request.CustomerId, request.PriceId, request.SeatCount),
                idempotencyKey,
                cancellationToken);
            return result is null ? Results.NotFound() : MutationResult(result);
        }
        catch (SubscriptionConcurrencyException exception)
        {
            return ConflictError(exception);
        }
        catch (IdempotencyConflictException exception)
        {
            return ConflictError(exception);
        }
        catch (ArgumentException exception)
        {
            return ValidationError(exception);
        }
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        return Results.Ok(await subscriptionService.ListAsync(organizationId, cancellationToken));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid subscriptionId,
        ClaimsPrincipal principal,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        var subscription = await subscriptionService.GetAsync(
            organizationId,
            subscriptionId,
            cancellationToken);
        return subscription is null ? Results.NotFound() : Results.Ok(subscription);
    }

    private static async Task<IResult> PreviewAsync(
        Guid subscriptionId,
        SubscriptionChangeRequest request,
        ClaimsPrincipal principal,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        try
        {
            var receipt = await subscriptionService.PreviewChangeAsync(
                organizationId,
                subscriptionId,
                new SubscriptionChangeCommand(request.NewPriceId, request.NewSeatCount),
                cancellationToken);
            return receipt is null ? Results.NotFound() : Results.Ok(receipt);
        }
        catch (SubscriptionDomainException exception)
        {
            return ConflictError(exception);
        }
        catch (ArgumentException exception)
        {
            return ValidationError(exception);
        }
    }

    private static async Task<IResult> ApplyAsync(
        Guid subscriptionId,
        SubscriptionChangeRequest request,
        ClaimsPrincipal principal,
        HttpRequest httpRequest,
        ISubscriptionChangeBillingOrchestrator billingOrchestrator,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        if (!TryGetIdempotencyKey(httpRequest, out var idempotencyKey))
        {
            return MissingIdempotencyKey();
        }

        try
        {
            var result = await billingOrchestrator.ApplyAsync(
                organizationId,
                subscriptionId,
                new SubscriptionChangeCommand(
                    request.NewPriceId,
                    request.NewSeatCount,
                    request.CardNumber),
                idempotencyKey,
                cancellationToken);
            return result is null ? Results.NotFound() : MutationResult(result);
        }
        catch (SubscriptionConcurrencyException exception)
        {
            return ConflictError(exception);
        }
        catch (IdempotencyConflictException exception)
        {
            return ConflictError(exception);
        }
        catch (SubscriptionDomainException exception)
        {
            return ConflictError(exception);
        }
        catch (ArgumentException exception)
        {
            return ValidationError(exception);
        }
        catch (SubscriptionBillingConflictException exception)
        {
            return ConflictError(exception);
        }
    }

    private static Task<IResult> CancelAsync(
        Guid subscriptionId,
        ClaimsPrincipal principal,
        HttpRequest httpRequest,
        ISubscriptionMutationCoordinator mutationCoordinator,
        CancellationToken cancellationToken) =>
        ExecuteTransitionAsync(
            subscriptionId,
            principal,
            httpRequest,
            mutationCoordinator.CancelAsync,
            cancellationToken);

    private static Task<IResult> PauseAsync(
        Guid subscriptionId,
        ClaimsPrincipal principal,
        HttpRequest httpRequest,
        ISubscriptionMutationCoordinator mutationCoordinator,
        CancellationToken cancellationToken) =>
        ExecuteTransitionAsync(
            subscriptionId,
            principal,
            httpRequest,
            mutationCoordinator.PauseAsync,
            cancellationToken);

    private static Task<IResult> ResumeAsync(
        Guid subscriptionId,
        ClaimsPrincipal principal,
        HttpRequest httpRequest,
        ISubscriptionMutationCoordinator mutationCoordinator,
        CancellationToken cancellationToken) =>
        ExecuteTransitionAsync(
            subscriptionId,
            principal,
            httpRequest,
            mutationCoordinator.ResumeAsync,
            cancellationToken);

    private static async Task<IResult> ExecuteTransitionAsync(
        Guid subscriptionId,
        ClaimsPrincipal principal,
        HttpRequest httpRequest,
        Func<Guid, Guid, string, CancellationToken, Task<SubscriptionMutationResult<SubscriptionSummary>?>> transition,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        if (!TryGetIdempotencyKey(httpRequest, out var idempotencyKey))
        {
            return MissingIdempotencyKey();
        }

        try
        {
            var result = await transition(
                organizationId,
                subscriptionId,
                idempotencyKey,
                cancellationToken);
            return result is null ? Results.NotFound() : MutationResult(result);
        }
        catch (SubscriptionConcurrencyException exception)
        {
            return ConflictError(exception);
        }
        catch (IdempotencyConflictException exception)
        {
            return ConflictError(exception);
        }
        catch (SubscriptionDomainException exception)
        {
            return ConflictError(exception);
        }
    }

    private static IResult MutationResult<T>(SubscriptionMutationResult<T> result)
    {
        if (result.Replayed)
        {
            return new StoredJsonResult(
                result.ResponseBody,
                result.StatusCode,
                result.Location);
        }

        return result.StatusCode == StatusCodes.Status201Created
            ? Results.Created(result.Location!, result.Value)
            : Results.Ok(result.Value);
    }

    private static IResult MissingIdempotencyKey() =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "The Idempotency-Key header is required for subscription mutations.");

    private static IResult ConflictError(Exception exception) =>
        Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: exception.Message);

    private static IResult ValidationError(ArgumentException exception) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["subscription"] = [exception.Message]
        });

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

    private static bool TryGetOrganizationId(
        ClaimsPrincipal principal,
        out Guid organizationId) =>
        Guid.TryParse(principal.FindFirstValue("org_id"), out organizationId);

    private sealed record CreateSubscriptionRequest(
        Guid CustomerId,
        Guid PriceId,
        int? SeatCount = null);

    private sealed record SubscriptionChangeRequest(
        Guid? NewPriceId = null,
        int? NewSeatCount = null,
        string? CardNumber = null);

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
