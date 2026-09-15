namespace BillingPlatform.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ApiSecurityHeadersTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task Live_health_response_includes_platform_security_headers()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://billing-platform.test")
        });

        using var response = await client.GetAsync("/health/live");

        response.EnsureSuccessStatusCode();
        Assert.Equal("nosniff", HeaderValue("X-Content-Type-Options"));
        Assert.Equal("DENY", HeaderValue("X-Frame-Options"));
        Assert.Equal("strict-origin-when-cross-origin", HeaderValue("Referrer-Policy"));
        Assert.Equal(
            "default-src 'self'; frame-ancestors 'none'; object-src 'none'",
            HeaderValue("Content-Security-Policy"));
        Assert.Contains("max-age=", HeaderValue("Strict-Transport-Security"));

        string HeaderValue(string name) =>
            Assert.Single(response.Headers.GetValues(name));
    }
}
