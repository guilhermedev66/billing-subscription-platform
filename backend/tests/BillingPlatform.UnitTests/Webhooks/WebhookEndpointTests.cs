using BillingPlatform.Webhooks.Domain;

namespace BillingPlatform.UnitTests.Webhooks;

public sealed class WebhookEndpointTests
{
    [Fact]
    public void Register_rejects_null_event_types_with_a_validation_error()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            WebhookEndpoint.Register(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "https://example.com/webhooks",
                new string('s', 32),
                null,
                DateTimeOffset.UtcNow));

        Assert.Contains("event types are required", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(257)]
    public void Register_rejects_secrets_outside_the_supported_length(int length)
    {
        var exception = Assert.Throws<ArgumentException>(() => RegisterWithSecret(new string('s', length)));

        Assert.Contains("between 32 and 256 characters", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(32)]
    [InlineData(256)]
    public void Register_accepts_secrets_within_the_supported_length(int length)
    {
        var secret = new string('s', length);

        var endpoint = RegisterWithSecret(secret);

        Assert.Equal(secret, endpoint.Secret);
    }

    private static WebhookEndpoint RegisterWithSecret(string secret) =>
        WebhookEndpoint.Register(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "https://example.com/webhooks",
            secret,
            ["invoice.paid"],
            DateTimeOffset.UtcNow);
}
