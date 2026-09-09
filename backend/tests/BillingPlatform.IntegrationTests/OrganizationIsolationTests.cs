using System.Net;
using System.Net.Http.Json;
using BillingPlatform.Identity.Application;
using Microsoft.Extensions.DependencyInjection;

namespace BillingPlatform.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class OrganizationIsolationTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task Organization_A_cannot_read_or_target_organization_B()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organizationA = await ApiTestClient.RegisterAsync(
            client,
            "Organization A",
            ApiTestClient.UniqueEmail("tenant-a"));
        var organizationB = await ApiTestClient.RegisterAsync(
            client,
            "Organization B",
            ApiTestClient.UniqueEmail("tenant-b"));

        using (var crossTenantRead = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Get,
                   $"/api/organizations/{organizationB.User.OrganizationId}",
                   organizationA.Token))
        using (var crossTenantResponse = await client.SendAsync(crossTenantRead))
        {
            Assert.Equal(HttpStatusCode.Forbidden, crossTenantResponse.StatusCode);
        }

        var forgedToken = CreateTokenWithMismatchedOrganization(
            factory,
            organizationA.User,
            organizationB.User.OrganizationId);
        using (var forgedRead = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Get,
                   $"/api/organizations/{organizationB.User.OrganizationId}",
                   forgedToken))
        using (var forgedResponse = await client.SendAsync(forgedRead))
        {
            Assert.Equal(HttpStatusCode.NotFound, forgedResponse.StatusCode);
        }

        using var writeAttempt = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            "/api/organizations",
            organizationA.Token,
            JsonContent.Create(new
            {
                organizationId = organizationB.User.OrganizationId,
                name = "Organization A Secondary",
                defaultCurrency = "USD",
                invoicePrefix = "INV",
                simulationModeEnabled = true
            }));
        using var writeResponse = await client.SendAsync(writeAttempt);

        Assert.Equal(HttpStatusCode.Created, writeResponse.StatusCode);
        var createdForA = await writeResponse.Content.ReadFromJsonAsync<OrganizationResponse>();
        Assert.NotNull(createdForA);
        Assert.NotEqual(organizationB.User.OrganizationId, createdForA.Id);

        using var verifyB = ApiTestClient.AuthorizedRequest(
            HttpMethod.Get,
            $"/api/organizations/{organizationB.User.OrganizationId}",
            organizationB.Token);
        using var verifyBResponse = await client.SendAsync(verifyB);

        Assert.Equal(HttpStatusCode.OK, verifyBResponse.StatusCode);
        var unchangedB = await verifyBResponse.Content.ReadFromJsonAsync<OrganizationResponse>();
        Assert.NotNull(unchangedB);
        Assert.Equal("Organization B", unchangedB.Name);
    }

    private static string CreateTokenWithMismatchedOrganization(
        BillingPlatformApiFactory factory,
        AuthUserResponse user,
        Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        return tokenService.Create(
            new IdentityUserInfo(user.Id, user.Email),
            organizationId).Token;
    }
}
