using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.Services;

public interface INetworkConnectivityService
{
    Task<bool> IsInternetAvailableAsync(CancellationToken cancellationToken = default);
}

internal sealed class NetworkConnectivityService(
    IHttpClientFactory httpClientFactory,
    ILogger<NetworkConnectivityService> logger) : INetworkConnectivityService
{
    public async Task<bool> IsInternetAvailableAsync(CancellationToken cancellationToken = default)
    {
        // Use anonymous HTTPS requests: a connected network adapter alone does not prove internet access.
        using var client = httpClientFactory.CreateClient(nameof(NetworkConnectivityService));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        var kugou = ProbeAsync(client, "https://www.kugou.com/", timeout.Token);
        var fallback = ProbeAsync(client, "https://www.baidu.com/", timeout.Token);
        var first = await Task.WhenAny(kugou, fallback);
        var available = await first || await (first == kugou ? fallback : kugou);
        await timeout.CancelAsync();
        // Observe both tasks before disposing the client and cancellation source.
        await Task.WhenAll(kugou, fallback);
        cancellationToken.ThrowIfCancellationRequested();
        return available;
    }

    private async Task<bool> ProbeAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            logger.LogDebug(ex, "网络连通性检查失败: {Url}", url);
            return false;
        }
    }
}
