using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

namespace KuGou.Net.Infrastructure.Http;

public interface IKgTransport
{
    Task<JsonElement> SendAsync(KgRequest request);
}

public class KgHttpTransport(HttpClient client) : IKgTransport
{
    public async Task<JsonElement> SendAsync(KgRequest request)
    {
        var baseUrl = request.BaseUrl ?? "https://gateway.kugou.com";
        var urlBuilder = new StringBuilder($"{baseUrl.TrimEnd('/')}/{request.Path.TrimStart('/')}");

        if (request.Params != null && request.Params.Count > 0)
        {
            urlBuilder.Append('?');
            var queryString = string.Join("&", request.Params.Select(kv =>
                $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
            urlBuilder.Append(queryString);
        }

        using var msg = new HttpRequestMessage(request.Method, urlBuilder.ToString());
        msg.Options.Set(new HttpRequestOptionsKey<KgRequest>("KgRequestDetail"), request);

        if (request.CustomHeaders != null)
            foreach (var kv in request.CustomHeaders)
                msg.Headers.TryAddWithoutValidation(kv.Key, kv.Value);

        if (request.Method == HttpMethod.Post)
        {
            if (!string.IsNullOrEmpty(request.RawBody))
            {
                msg.Content = new StringContent(request.RawBody, Encoding.UTF8, request.ContentType);
            }
            else if (request.Body != null)
            {
                var jsonBody = JsonSerializer.Serialize(
                    request.Body,
                    AppJsonContext.Default.JsonObject
                );
                msg.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            }
        }

        using var response = await client.SendAsync(msg);
        response.EnsureSuccessStatusCode();

        var responseBytes = await response.Content.ReadAsByteArrayAsync();

        if (responseBytes.Length == 0)
            return JsonSerializer.Deserialize<JsonElement>("{}");

        try
        {
            using var doc = JsonDocument.Parse(responseBytes);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            var base64Content = Convert.ToBase64String(responseBytes);

            var fallbackJson = new JsonObject
            {
                ["__raw_base64__"] = base64Content
            };

            var fallbackDoc = JsonSerializer.SerializeToElement(fallbackJson, AppJsonContext.Default.JsonObject);
            return fallbackDoc;
        }
    }
}