using System.Net;
using BillingPlatform.Webhooks.Infrastructure;

namespace BillingPlatform.UnitTests.Webhooks;

public sealed class WebhookDestinationResolverTests
{
    private readonly WebhookDestinationResolver resolver = new();

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.0.1")]
    [InlineData("198.18.0.1")]
    [InlineData("203.0.113.1")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("2001:db8::1")]
    public async Task Production_rejects_non_global_destinations(string address)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            resolver.ResolveAllowedAsync(address, false, CancellationToken.None));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("2606:4700:4700::1111")]
    public async Task Production_accepts_global_unicast_destinations(string address)
    {
        var resolved = await resolver.ResolveAllowedAsync(address, false, CancellationToken.None);

        Assert.Equal(IPAddress.Parse(address), Assert.Single(resolved));
    }

    [Fact]
    public async Task Testing_can_connect_to_loopback_destinations()
    {
        var resolved = await resolver.ResolveAllowedAsync("127.0.0.1", true, CancellationToken.None);

        Assert.Equal(IPAddress.Loopback, Assert.Single(resolved));
    }
}
