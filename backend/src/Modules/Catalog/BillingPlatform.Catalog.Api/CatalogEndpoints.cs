using System.Security.Claims;
using BillingPlatform.Catalog.Application;
using BillingPlatform.Catalog.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BillingPlatform.Catalog.Api;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/catalog")
            .RequireAuthorization();

        MapProductEndpoints(group.MapGroup("/products").WithTags("Products"));
        MapPriceEndpoints(group.MapGroup("/prices").WithTags("Prices"));

        return endpoints;
    }

    private static void MapProductEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/", CreateProductAsync);
        group.MapGet("/", ListProductsAsync);
        group.MapGet("/{productId:guid}", GetProductByIdAsync);
        group.MapPut("/{productId:guid}", UpdateProductAsync);
        group.MapDelete("/{productId:guid}", DeleteProductAsync);
    }

    private static void MapPriceEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/", CreatePriceAsync);
        group.MapGet("/", ListPricesAsync);
        group.MapGet("/{priceId:guid}", GetPriceByIdAsync);
        group.MapPut("/{priceId:guid}", UpdatePriceAsync);
        group.MapDelete("/{priceId:guid}", DeletePriceAsync);
    }

    private static async Task<IResult> CreateProductAsync(
        ProductRequest request,
        ClaimsPrincipal principal,
        IProductService productService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        try
        {
            var product = await productService.CreateAsync(
                organizationId,
                new CreateProductCommand(request.Name, request.Description, request.Active),
                cancellationToken);
            return Results.Created($"/api/catalog/products/{product.Id}", product);
        }
        catch (ArgumentException exception)
        {
            return ValidationError(exception);
        }
    }

    private static async Task<IResult> ListProductsAsync(
        ClaimsPrincipal principal,
        IProductService productService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        return Results.Ok(await productService.ListAsync(organizationId, cancellationToken));
    }

    private static async Task<IResult> GetProductByIdAsync(
        Guid productId,
        ClaimsPrincipal principal,
        IProductService productService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        var product = await productService.GetAsync(organizationId, productId, cancellationToken);
        if (product is not null)
        {
            return Results.Ok(product);
        }

        return Results.NotFound();
    }

    private static async Task<IResult> UpdateProductAsync(
        Guid productId,
        ProductRequest request,
        ClaimsPrincipal principal,
        IProductService productService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        try
        {
            var product = await productService.UpdateAsync(
                organizationId,
                productId,
                new UpdateProductCommand(request.Name, request.Description, request.Active),
                cancellationToken);
            if (product is not null)
            {
                return Results.Ok(product);
            }

            return Results.NotFound();
        }
        catch (ArgumentException exception)
        {
            return ValidationError(exception);
        }
    }

    private static async Task<IResult> DeleteProductAsync(
        Guid productId,
        ClaimsPrincipal principal,
        IProductService productService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        if (await productService.DeleteAsync(organizationId, productId, cancellationToken))
        {
            return Results.NoContent();
        }

        return Results.NotFound();
    }

    private static async Task<IResult> CreatePriceAsync(
        PriceRequest request,
        ClaimsPrincipal principal,
        IPriceService priceService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        try
        {
            var price = await priceService.CreateAsync(
                organizationId,
                ToCreatePriceCommand(request),
                cancellationToken);
            if (price is not null)
            {
                return Results.Created($"/api/catalog/prices/{price.Id}", price);
            }

            return Results.NotFound();
        }
        catch (ArgumentException exception)
        {
            return ValidationError(exception);
        }
    }

    private static async Task<IResult> ListPricesAsync(
        Guid? productId,
        ClaimsPrincipal principal,
        IPriceService priceService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        return Results.Ok(await priceService.ListAsync(organizationId, productId, cancellationToken));
    }

    private static async Task<IResult> GetPriceByIdAsync(
        Guid priceId,
        ClaimsPrincipal principal,
        IPriceService priceService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        var price = await priceService.GetAsync(organizationId, priceId, cancellationToken);
        if (price is not null)
        {
            return Results.Ok(price);
        }

        return Results.NotFound();
    }

    private static async Task<IResult> UpdatePriceAsync(
        Guid priceId,
        PriceRequest request,
        ClaimsPrincipal principal,
        IPriceService priceService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        try
        {
            var price = await priceService.UpdateAsync(
                organizationId,
                priceId,
                ToUpdatePriceCommand(request),
                cancellationToken);
            if (price is not null)
            {
                return Results.Ok(price);
            }

            return Results.NotFound();
        }
        catch (ArgumentException exception)
        {
            return ValidationError(exception);
        }
    }

    private static async Task<IResult> DeletePriceAsync(
        Guid priceId,
        ClaimsPrincipal principal,
        IPriceService priceService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(principal, out var organizationId))
        {
            return Results.Forbid();
        }

        if (await priceService.DeleteAsync(organizationId, priceId, cancellationToken))
        {
            return Results.NoContent();
        }

        return Results.NotFound();
    }

    private static CreatePriceCommand ToCreatePriceCommand(PriceRequest request) =>
        new(
            request.ProductId,
            request.PricingModel,
            request.Currency,
            request.BillingInterval,
            request.FlatUnitAmountCents,
            request.PerSeatUnitAmountCents,
            request.MeteredUnitAmountCents,
            request.MeteredAggregation,
            request.Tiers ?? [],
            request.TrialDays);

    private static UpdatePriceCommand ToUpdatePriceCommand(PriceRequest request) =>
        new(
            request.PricingModel,
            request.Currency,
            request.BillingInterval,
            request.FlatUnitAmountCents,
            request.PerSeatUnitAmountCents,
            request.MeteredUnitAmountCents,
            request.MeteredAggregation,
            request.Tiers ?? [],
            request.TrialDays);

    private static IResult ValidationError(ArgumentException exception) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["catalog"] = [exception.Message]
        });

    private static bool TryGetOrganizationId(
        ClaimsPrincipal principal,
        out Guid organizationId) =>
        Guid.TryParse(principal.FindFirstValue("org_id"), out organizationId);

    private sealed record ProductRequest(
        string Name,
        string Description = "",
        bool Active = true);

    private sealed record PriceRequest(
        Guid ProductId,
        PricingModel PricingModel,
        string Currency,
        BillingInterval BillingInterval,
        long? FlatUnitAmountCents = null,
        long? PerSeatUnitAmountCents = null,
        long? MeteredUnitAmountCents = null,
        MeteredAggregation? MeteredAggregation = null,
        IReadOnlyList<PricingTierInput>? Tiers = null,
        int? TrialDays = null);
}
