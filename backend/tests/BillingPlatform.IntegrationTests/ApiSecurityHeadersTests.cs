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

    [Fact]
    public async Task Hsts_header_is_still_added_behind_a_reverse_proxy_terminating_tls()
    {
        // Render (the real production host) terminates TLS at its edge and forwards plain HTTP
        // to this container, so the request itself arrives as HTTP with X-Forwarded-Proto: https.
        // Without forwarded-headers handling, Request.IsHttps is always false in that shape and
        // UseHsts() silently never emits the header, even though the code calls it correctly.
        await using var factory = database.CreateFactory();
        // A non-localhost host, because HstsMiddleware's default ExcludedHosts
        // ("localhost", "127.0.0.1", "[::1]") would otherwise mask this regression.
        using var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("http://billing-platform.test")
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "203.0.113.5");

        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.Contains("max-age=", Assert.Single(
            response.Headers.GetValues("Strict-Transport-Security")));
    }
}
