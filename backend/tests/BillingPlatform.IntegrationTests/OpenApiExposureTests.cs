using System.Net;

namespace BillingPlatform.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class OpenApiExposureTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task Openapi_document_is_not_exposed_outside_development()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
