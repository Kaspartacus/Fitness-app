using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Options;

namespace FitnessApp.Infrastructure.Notifications;

public sealed class WebPushOptions
{
    public string PublicKey { get; set; } = "";
    public string PrivateKey { get; set; } = "";
    public string Subject { get; set; } = "";
    public bool Configured => PublicKey.Length > 0 && PrivateKey.Length > 0 && Subject.Length > 0;
}

public static class PushEndpointPolicy
{
    public static bool IsAllowed(string? endpoint) => endpoint is { Length: <= 2048 } &&
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.Port == 443 && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        uri.AbsolutePath.Length > 1 && uri.HostNameType == UriHostNameType.Dns &&
        (uri.IdnHost == "fcm.googleapis.com" || uri.IdnHost == "updates.push.services.mozilla.com" ||
         uri.IdnHost == "web.push.apple.com" || uri.IdnHost.EndsWith(".push.apple.com", StringComparison.Ordinal));

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (b.Length == 16) return (b[0] & 0xe0) == 0x20; // Global unicast only.
        return b.Length == 4 && b[0] is not (0 or 10 or 127) && b[0] < 224 &&
            !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] >= 16 && b[1] <= 31) &&
            !(b[0] == 192 && (b[1] == 168 || b[1] == 0 || b[1] == 2)) &&
            !(b[0] == 100 && b[1] >= 64 && b[1] <= 127) &&
            !(b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51 && b[2] == 100)) &&
            !(b[0] == 203 && b[1] == 0 && b[2] == 113);
    }

    public static bool ValidKey(string? value, int bytes)
    {
        if (value is null || value.Length > 100 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) return false;
        try { return Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '=')).Length == bytes; }
        catch (FormatException) { return false; }
    }
}

public enum PushDeliveryResult { Delivered, Expired, Retry, Rejected }
public interface IWebPushSender
{
    Task<PushDeliveryResult> SendAsync(WebPushSubscription subscription, string payload, string topic, int ttl, CancellationToken ct);
}

public sealed class WebPushSender : IWebPushSender, IDisposable
{
    private readonly PushServiceClient client;
    private readonly HttpClient httpClient;
    public WebPushSender(IOptions<WebPushOptions> options)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseProxy = false,
            ConnectCallback = async (context, ct) =>
            {
                // Resolve and connect to the validated address directly; no second DNS lookup/rebinding.
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
                var address = addresses.FirstOrDefault(PushEndpointPolicy.IsPublicAddress)
                    ?? throw new HttpRequestException("Push service address is not public.");
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(new IPEndPoint(address, 443), ct); return new NetworkStream(socket, ownsSocket: true); }
                catch { socket.Dispose(); throw; }
            }
        };
        httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        client = new PushServiceClient(httpClient) { AutoRetryAfter = false };
        var config = options.Value;
        if (config.Configured)
            client.DefaultAuthentication = new VapidAuthentication(config.PublicKey, config.PrivateKey) { Subject = config.Subject };
    }
    public async Task<PushDeliveryResult> SendAsync(WebPushSubscription subscription, string payload, string topic, int ttl, CancellationToken ct)
    {
        if (!PushEndpointPolicy.IsAllowed(subscription.Endpoint)) return PushDeliveryResult.Rejected;
        try
        {
            await client.RequestPushMessageDeliveryAsync(new PushSubscription
            {
                Endpoint = subscription.Endpoint,
                Keys = new Dictionary<string, string> { ["p256dh"] = subscription.P256dh, ["auth"] = subscription.Auth }
            }, new PushMessage(payload) { Topic = topic, TimeToLive = ttl }, ct);
            return PushDeliveryResult.Delivered;
        }
        catch (PushServiceClientException ex)
        {
            return ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound ? PushDeliveryResult.Expired :
                (int)ex.StatusCode >= 500 || ex.StatusCode is HttpStatusCode.TooManyRequests ? PushDeliveryResult.Retry : PushDeliveryResult.Rejected;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return PushDeliveryResult.Retry; }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException or FormatException) { return PushDeliveryResult.Rejected; }
    }
    public void Dispose() => httpClient.Dispose();
}
