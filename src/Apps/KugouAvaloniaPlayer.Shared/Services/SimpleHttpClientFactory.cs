using System.Net.Http;
using KuGou.Net.Infrastructure.Http;

namespace KugouAvaloniaPlayer.Services;

public sealed class SimpleHttpClientFactory : IHttpClientFactory
{
    // 共享底层 handler,避免每次 CreateClient 都新建连接池
    private static readonly SocketsHttpHandler SharedHandler = KgPrimaryHandler.Create();

    public HttpClient CreateClient(string name)
    {
        return new HttpClient(SharedHandler, disposeHandler: false);
    }
}
