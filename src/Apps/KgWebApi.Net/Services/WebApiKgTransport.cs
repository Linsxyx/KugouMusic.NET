using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Infrastructure.Http.Handlers;
using KuGou.Net.Protocol.Transport;
using System.Net;
using System.Text.Json;

namespace KgWebApi.Net.Services;

public sealed class WebApiKgTransport : IKgTransport, IDisposable
{
    private readonly HttpClient _client;
    private readonly KgHttpTransport _transport;

    public WebApiKgTransport(
        CookieContainer cookieContainer,
        KgSignatureHandler signatureHandler,
        IHttpMessageHandlerFactory messageHandlerFactory)
    {
        var pooledHandler = messageHandlerFactory.CreateHandler(WebApiKgHttpClientNames.KuGou);
        var cookieHandler = new WebApiCookieContainerHandler(cookieContainer)
        {
            InnerHandler = pooledHandler
        };

        signatureHandler.InnerHandler = cookieHandler;

        _client = new HttpClient(signatureHandler, disposeHandler: false);
        _transport = new KgHttpTransport(_client);
    }

    public Task<JsonElement> SendAsync(KgRequest request)
    {
        return _transport.SendAsync(request);
    }

    public Task<byte[]> SendBytesAsync(KgRequest request)
    {
        return _transport.SendBytesAsync(request);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
