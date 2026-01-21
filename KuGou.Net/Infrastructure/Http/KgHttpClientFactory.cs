using System.Net;
using KuGou.Net.Infrastructure.Http.Handlers;
using KuGou.Net.Protocol.Session;

namespace KuGou.Net.Infrastructure.Http;

/// <summary>
///     负责组装 HttpClient 的工厂。
///     既可以在非 DI 环境下手动创建 Transport，也可以包含 DI 的配置逻辑。
/// </summary>
public static class KgHttpClientFactory
{
    public static (IKgTransport Transport, KgSessionManager Session) CreateWithSession()
    {
        var cookieContainer = new CookieContainer();
        var sessionManager = new KgSessionManager(cookieContainer);

        // 调用之前的 Create 方法 (假设你保留了 Create(KgSessionManager existing))
        var transport = Create(sessionManager);

        return (transport, sessionManager);
    }

    /// <summary>
    ///     [非 DI 模式] 手动创建一个配置好的 Transport。
    ///     自动组装 CookieContainer -> SessionManager -> SignatureHandler -> HttpClient
    /// </summary>
    /// <param name="existingSession">如果需要在多个 Client 间共享 Session，可传入已有的 SessionManager</param>
    /// <returns>配置好的传输层对象</returns>
    public static IKgTransport Create(KgSessionManager? existingSession = null)
    {
        var cookieContainer = new CookieContainer();
        var sessionManager = existingSession ?? new KgSessionManager(new CookieContainer());

        var primaryHandler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = cookieContainer,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ServerCertificateCustomValidationCallback = delegate { return true; }
        };


        var signatureHandler = new KgSignatureHandler(sessionManager)
        {
            InnerHandler = primaryHandler
        };

        var httpClient = new HttpClient(signatureHandler);

        httpClient.DefaultRequestHeaders.Connection.Add("keep-alive");

        return new KgHttpTransport(httpClient);
    }
}