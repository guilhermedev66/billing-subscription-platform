using System.Net;
using System.Net.Http.Json;
using BillingPlatform.Organizations.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BillingPlatform.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class AuthenticationFlowTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task Register_then_login_then_me_returns_the_same_tenant_session()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var email = ApiTestClient.UniqueEmail("auth-flow");
        const string organizationName = "Auth Flow Organization";

        var registration = await ApiTestClient.RegisterAsync(
            client,
            organizationName,
            email);
        var login = await ApiTestClient.LoginAsync(client, email);

        Assert.Equal(email, registration.User.Email);
        Assert.Equal(organizationName, registration.User.OrganizationName);
        Assert.Equal(registration.User, login.User);

        using var retiredTokenResponse = await client.PostAsJsonAsync("/api/auth/token", new
        {
            email,
            password = ApiTestClient.ValidPassword
        });
        Assert.Equal(HttpStatusCode.NotFound, retiredTokenResponse.StatusCode);

        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Get,
            "/api/auth/me",
            login.Token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var currentUser = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(currentUser);
        Assert.Equal(login.User.Id, currentUser.UserId);
        Assert.Equal(login.User.Email, currentUser.Email);
        Assert.Equal(login.User.OrganizationId, currentUser.OrganizationId);
    }

    [Fact]
    public async Task Login_rejects_requests_after_the_fixed_window_limit()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var payload = new
        {
            email = ApiTestClient.UniqueEmail("rate-limit"),
            password = "WrongPassword!123"
        };

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await client.PostAsJsonAsync("/api/auth/login", payload);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var rejectedResponse = await client.PostAsJsonAsync("/api/auth/login", payload);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedResponse.StatusCode);
    }

    [Fact]
    public async Task Register_rolls_back_identity_when_organization_creation_fails()
    {
        var email = ApiTestClient.UniqueEmail("register-rollback");
        await using (var failingFactory = database.CreateFactory(services =>
                     {
                         services.RemoveAll<IOrganizationService>();
                         services.AddScoped<IOrganizationService, FailingOrganizationService>();
                     }))
        using (var failingClient = failingFactory.CreateClient())
        using (var failedResponse = await failingClient.PostAsJsonAsync("/api/auth/register", new
               {
                   organizationName = "Rollback Organization",
                   email,
                   password = ApiTestClient.ValidPassword
               }))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, failedResponse.StatusCode);
        }

        await using var retryFactory = database.CreateFactory();
        using var retryClient = retryFactory.CreateClient();
        var retrySession = await ApiTestClient.RegisterAsync(
            retryClient,
            "Rollback Organization",
            email);

        Assert.Equal(email, retrySession.User.Email);
    }

    private sealed class FailingOrganizationService : IOrganizationService
    {
        public Task<OrganizationSummary> CreateAsync(
            Guid userId,
            CreateOrganizationCommand command,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated organization persistence failure.");

        public Task<OrganizationSummary?> GetAsync(
            Guid userId,
            Guid organizationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<OrganizationSummary?>(null);
    }
}
