using System.Net;
using System.Net.Http.Json;

namespace BillingPlatform.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class CustomersIsolationTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task Customer_reads_and_updates_are_tenant_isolated()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organizationA = await ApiTestClient.RegisterAsync(
            client,
            "Customer Organization A",
            ApiTestClient.UniqueEmail("customer-a"));
        var organizationB = await ApiTestClient.RegisterAsync(
            client,
            "Customer Organization B",
            ApiTestClient.UniqueEmail("customer-b"));

        using var create = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            "/api/customers/",
            organizationA.Token,
            JsonContent.Create(new
            {
                name = "A Customer",
                email = "customer-a@customers.example"
            }));
        using var createResponse = await client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var customer = await createResponse.Content.ReadFromJsonAsync<CustomerResponse>();
        Assert.NotNull(customer);
        Assert.Equal(0, customer.BalanceCents);
        Assert.False(customer.DelinquentFlag);

        using (var duplicateCreate = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Post,
                   "/api/customers/",
                   organizationA.Token,
                   JsonContent.Create(new
                   {
                       name = "Duplicate Customer",
                       email = "customer-a@customers.example"
                   })))
        using (var duplicateResponse = await client.SendAsync(duplicateCreate))
        {
            Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
            var problem = await duplicateResponse.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
            Assert.NotNull(problem);
            Assert.Equal(409, problem.Status);
            Assert.Equal("A customer with this email already exists.", problem.Title);
        }

        using (var update = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Put,
                   $"/api/customers/{customer.Id}",
                   organizationA.Token,
                   JsonContent.Create(new
                   {
                       name = "Updated Customer",
                       email = "updated@customers.example"
                   })))
        using (var updateResponse = await client.SendAsync(update))
        {
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            var updatedCustomer = await updateResponse.Content.ReadFromJsonAsync<CustomerResponse>();
            Assert.NotNull(updatedCustomer);
            Assert.Equal(0, updatedCustomer.BalanceCents);
            Assert.False(updatedCustomer.DelinquentFlag);
        }

        using (var list = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Get,
                   "/api/customers/",
                   organizationB.Token))
        using (var listResponse = await client.SendAsync(list))
        {
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            var customers = await listResponse.Content.ReadFromJsonAsync<CustomerResponse[]>();
            Assert.NotNull(customers);
            Assert.Empty(customers);
        }

        using (var crossTenantRead = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Get,
                   $"/api/customers/{customer.Id}",
                   organizationB.Token))
        using (var crossTenantResponse = await client.SendAsync(crossTenantRead))
        {
            Assert.Equal(HttpStatusCode.NotFound, crossTenantResponse.StatusCode);
        }

        using (var crossTenantUpdate = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Put,
                   $"/api/customers/{customer.Id}",
                   organizationB.Token,
                   JsonContent.Create(new
                   {
                       name = "Should Not Update",
                       email = "blocked@customers.example"
                   })))
        using (var crossTenantResponse = await client.SendAsync(crossTenantUpdate))
        {
            Assert.Equal(HttpStatusCode.NotFound, crossTenantResponse.StatusCode);
        }
    }
}

internal sealed record CustomerResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string Email,
    long BalanceCents,
    bool DelinquentFlag,
    DateTimeOffset CreatedAt);

internal sealed record ProblemDetailsResponse(int Status, string Title);
