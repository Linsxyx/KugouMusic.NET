using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Infrastructure.Http.Handlers;
using KuGou.Net.Protocol.Transport;
using System.Text.Json;

namespace KgWebApi.Net.Services;

public sealed class WebApiKgTransport : IKgTransport
{
    private readonly KgHttpTransport _transport;

    public WebApiKgTransport(IHttpClientFactory httpClientFactory, KgSignatureHandler signatureHandler)
    {
        var client = httpClientFactory.CreateClient(WebApiKuGouHttpClientNames.Outbound);
        _transport = new KgHttpTransport(client, signatureHandler.ApplyAsync);
    }

    public Task<JsonElement> SendAsync(KgRequest request)
    {
        return _transport.SendAsync(request);
    }
}
