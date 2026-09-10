using System.Net;
using System.Net.Http.Json;

namespace BillingPlatform.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class CatalogIsolationTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task Products_and_prices_are_tenant_isolated()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organizationA = await ApiTestClient.RegisterAsync(
            client,
            "Catalog Organization A",
            ApiTestClient.UniqueEmail("catalog-a"));
        var organizationB = await ApiTestClient.RegisterAsync(
            client,
            "Catalog Organization B",
            ApiTestClient.UniqueEmail("catalog-b"));

        using var createProduct = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            "/api/catalog/products/",
            organizationA.Token,
            JsonContent.Create(new
            {
                name = "A Product",
                description = "A tenant-owned product",
                active = true
            }));
        using var createProductResponse = await client.SendAsync(createProduct);
        Assert.Equal(HttpStatusCode.Created, createProductResponse.StatusCode);
        var product = await createProductResponse.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.NotNull(product);

        using var createPrice = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            "/api/catalog/prices/",
            organizationA.Token,
            JsonContent.Create(new
            {
                productId = product.Id,
                pricingModel = 0,
                currency = "USD",
                billingInterval = 0,
                flatUnitAmountCents = 2900,
                trialDays = 14
            }));
        using var createPriceResponse = await client.SendAsync(createPrice);
        Assert.Equal(HttpStatusCode.Created, createPriceResponse.StatusCode);
        var price = await createPriceResponse.Content.ReadFromJsonAsync<PriceResponse>();
        Assert.NotNull(price);

        using (var productList = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Get,
                   "/api/catalog/products/",
                   organizationB.Token))
        using (var productListResponse = await client.SendAsync(productList))
        {
            Assert.Equal(HttpStatusCode.OK, productListResponse.StatusCode);
            var products = await productListResponse.Content.ReadFromJsonAsync<ProductResponse[]>();
            Assert.NotNull(products);
            Assert.Empty(products);
        }

        using (var priceList = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Get,
                   "/api/catalog/prices/",
                   organizationB.Token))
        using (var priceListResponse = await client.SendAsync(priceList))
        {
            Assert.Equal(HttpStatusCode.OK, priceListResponse.StatusCode);
            var prices = await priceListResponse.Content.ReadFromJsonAsync<PriceResponse[]>();
            Assert.NotNull(prices);
            Assert.Empty(prices);
        }

        using (var crossTenantProductRead = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Get,
                   $"/api/catalog/products/{product.Id}",
                   organizationB.Token))
        using (var crossTenantProductResponse = await client.SendAsync(crossTenantProductRead))
        {
            Assert.Equal(HttpStatusCode.NotFound, crossTenantProductResponse.StatusCode);
        }

        using (var crossTenantProductUpdate = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Put,
                   $"/api/catalog/products/{product.Id}",
                   organizationB.Token,
                   JsonContent.Create(new
                   {
                       name = "Should Not Update",
                       description = "blocked",
                       active = false
                   })))
        using (var crossTenantProductResponse = await client.SendAsync(crossTenantProductUpdate))
        {
            Assert.Equal(HttpStatusCode.NotFound, crossTenantProductResponse.StatusCode);
        }

        using (var crossTenantPriceRead = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Get,
                   $"/api/catalog/prices/{price.Id}",
                   organizationB.Token))
        using (var crossTenantPriceResponse = await client.SendAsync(crossTenantPriceRead))
        {
            Assert.Equal(HttpStatusCode.NotFound, crossTenantPriceResponse.StatusCode);
        }

        using (var crossTenantPriceUpdate = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Put,
                   $"/api/catalog/prices/{price.Id}",
                   organizationB.Token,
                   JsonContent.Create(new
                   {
                       productId = product.Id,
                       pricingModel = 0,
                       currency = "USD",
                       billingInterval = 0,
                       flatUnitAmountCents = 1
                   })))
        using (var crossTenantPriceResponse = await client.SendAsync(crossTenantPriceUpdate))
        {
            Assert.Equal(HttpStatusCode.NotFound, crossTenantPriceResponse.StatusCode);
        }
    }
}

internal sealed record ProductResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string Description,
    bool Active);

internal sealed record PriceResponse(
    Guid Id,
    Guid ProductId,
    Guid OrganizationId,
    int PricingModel,
    string Currency,
    int BillingInterval,
    long? FlatUnitAmountCents,
    long? PerSeatUnitAmountCents,
    long? MeteredUnitAmountCents,
    int? MeteredAggregation,
    object[] Tiers,
    int? TrialDays);
