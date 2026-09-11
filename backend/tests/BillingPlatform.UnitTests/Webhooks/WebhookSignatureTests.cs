using System.Text;
using BillingPlatform.Webhooks.Application;

namespace BillingPlatform.UnitTests.Webhooks;

public sealed class WebhookSignatureTests
{
    [Fact]
    public void Signature_covers_the_exact_raw_body_and_uses_constant_time_verification()
    {
        var now = new DateTimeOffset(2040, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var body = Encoding.UTF8.GetBytes("{\"b\":2, \"a\":1}\n");
        var header = WebhookSignature.CreateHeader("secret", now, body);

        Assert.True(WebhookSignature.Verify(header, "secret", body, now, out _));
        Assert.False(WebhookSignature.Verify(
            header, "secret", Encoding.UTF8.GetBytes("{\"a\":1,\"b\":2}"), now, out _));
    }

    [Fact]
    public void Replay_window_is_relative_to_the_supplied_virtual_now()
    {
        var virtualNow = new DateTimeOffset(2040, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var body = Encoding.UTF8.GetBytes("{\"simulated\":true}");
        var header = WebhookSignature.CreateHeader("secret", virtualNow, body);

        Assert.True(WebhookSignature.Verify(header, "secret", body, virtualNow, out _));
    }
}
