using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;

namespace BillingPlatform.Webhooks.Infrastructure;

internal interface IWebhookDestinationResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAllowedAsync(
        string host,
        bool allowPrivateAddresses,
        CancellationToken cancellationToken);
}

internal sealed class WebhookDestinationResolver : IWebhookDestinationResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAllowedAsync(
        string host,
        bool allowPrivateAddresses,
        CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);
        if (addresses.Length == 0 ||
            !allowPrivateAddresses && addresses.Any(IsPrivateOrLocalAddress))
        {
            throw new ArgumentException(
                "Webhook URL resolves to a private, local, or unavailable network address.",
                nameof(host));
        }

        return addresses;
    }

    private static bool IsPrivateOrLocalAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal ||
            address.IsIPv6SiteLocal || address.IsIPv6Multicast)
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   bytes[0] == 0 ||
                   bytes[0] == 100 && bytes[1] is >= 64 and <= 127 ||
                   bytes[0] == 169 && bytes[1] == 254 ||
                   bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                   bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0 ||
                   bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2 ||
                   bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99 ||
                   bytes[0] == 192 && bytes[1] == 168 ||
                   bytes[0] == 198 && bytes[1] is 18 or 19 ||
                   bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100 ||
                   bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113 ||
                   bytes[0] >= 224;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return IsPrivateOrLocalAddress(address.MapToIPv4());
        }

        var value = address.GetAddressBytes();
        if (value.Length != 16 || (value[0] & 0xe0) != 0x20)
        {
            return true;
        }

        return value[0] == 0x20 && value[1] == 0x01 && value[2] <= 0x01 ||
               value[0] == 0x20 && value[1] == 0x01 && value[2] == 0x0d && value[3] == 0xb8 ||
               value[0] == 0x20 && value[1] == 0x02 ||
               value[0] == 0x3f && value[1] == 0xff && (value[2] & 0xf0) == 0;
    }
}

internal static class WebhookHttpMessageHandlerFactory
{
    public static HttpMessageHandler Create(
        IWebhookDestinationResolver destinationResolver,
        IHostEnvironment environment)
    {
        var allowPrivateAddresses = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = async (context, cancellationToken) =>
            {
                var addresses = await destinationResolver.ResolveAllowedAsync(
                    context.DnsEndPoint.Host,
                    allowPrivateAddresses,
                    cancellationToken);
                SocketException? lastException = null;
                foreach (var address in addresses)
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                    {
                        NoDelay = true
                    };
                    try
                    {
                        await socket.ConnectAsync(
                            new IPEndPoint(address, context.DnsEndPoint.Port),
                            cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (SocketException exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        lastException = exception;
                        socket.Dispose();
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }

                throw new HttpRequestException(
                    $"Unable to connect to the validated webhook destination '{context.DnsEndPoint.Host}'.",
                    lastException);
            }
        };
    }
}
