using System.Net;
using System.Net.Sockets;

namespace KuGou.Net.Infrastructure.Http;

/// <summary>
///     构建底层 SocketsHttpHandler，连接时 IPv4 优先。
///     部分网络（如校园网）的 IPv6 链路对酷狗 CDN 是 TCP 黑洞，默认按 DNS 返回顺序
///     先试 IPv6 会让请求挂起直到超时；这里固定先连 IPv4，全部失败再用 IPv6 兜底。
/// </summary>
public static class KgPrimaryHandler
{
    private static readonly TimeSpan ConnectTimeoutPerAddress = TimeSpan.FromSeconds(5);

    public static SocketsHttpHandler Create(CookieContainer? cookieContainer = null)
    {
        return new SocketsHttpHandler
        {
            UseCookies = cookieContainer != null,
            CookieContainer = cookieContainer ?? new CookieContainer(),
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectCallback = ConnectPreferingIpv4
        };
    }

    private static async ValueTask<Stream> ConnectPreferingIpv4(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);

        var ipv4 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToArray();
        var ipv6 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6).ToArray();
        var ordered = ipv4.Length > 0 ? ipv4.Concat(ipv6).ToArray() : addresses;

        Exception? lastError = null;
        foreach (var address in ordered)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(ConnectTimeoutPerAddress);
                await socket.ConnectAsync(address, context.DnsEndPoint.Port, timeoutCts.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                socket.Dispose();
                throw;
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                lastError = ex;
            }
        }

        throw lastError ?? new SocketException((int)SocketError.HostNotFound);
    }
}
