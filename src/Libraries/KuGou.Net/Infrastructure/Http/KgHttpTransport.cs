using System.Text;
using System.Text.Json;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

namespace KuGou.Net.Infrastructure.Http;

public interface IKgTransport
{
    Task<JsonElement> SendAsync(KgRequest request);

    async Task<byte[]> SendBytesAsync(KgRequest request)
    {
        var response = await SendAsync(request);

        if (response.ValueKind == JsonValueKind.Object &&
            response.TryGetProperty("__raw_base64__", out var rawElement) &&
            rawElement.GetString() is { } rawBase64)
            return Convert.FromBase64String(rawBase64);

        return Encoding.UTF8.GetBytes(response.GetRawText());
    }
}

public class KgHttpTransport(HttpClient client) : IKgTransport
{
    private const int MaxGetAttempts = 3;

    public async Task<JsonElement> SendAsync(KgRequest request)
    {
        var requestUrl = BuildRequestUrl(request);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SendOnceAsync(request, requestUrl);
            }
            catch (HttpRequestException ex) when (
                request.Method == HttpMethod.Get &&
                attempt < MaxGetAttempts &&
                IsTransient(ex))
            {
                await Task.Delay(GetRetryDelay(attempt));
            }
        }
    }

    public async Task<byte[]> SendBytesAsync(KgRequest request)
    {
        var requestUrl = BuildRequestUrl(request);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SendBytesOnceAsync(request, requestUrl);
            }
            catch (HttpRequestException ex) when (
                request.Method == HttpMethod.Get &&
                attempt < MaxGetAttempts &&
                IsTransient(ex))
            {
                await Task.Delay(GetRetryDelay(attempt));
            }
        }
    }

    private async Task<JsonElement> SendOnceAsync(KgRequest request, string requestUrl)
    {
        using var msg = CreateRequestMessage(request, requestUrl);
        using var response = await client.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength == 0)
        {
            return JsonElement.Parse("{}");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(responseStream, AppJsonContext.Default.JsonElement);
    }

    private async Task<byte[]> SendBytesOnceAsync(KgRequest request, string requestUrl)
    {
        using var msg = CreateRequestMessage(request, requestUrl);
        using var response = await client.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync();
    }

    private static string BuildRequestUrl(KgRequest request)
    {
        var baseUrl = request.BaseUrl ?? "https://gateway.kugou.com";
        var urlBuilder = new StringBuilder($"{baseUrl.TrimEnd('/')}/{request.Path.TrimStart('/')}");

        if (request.Params.Count > 0)
        {
            urlBuilder.Append('?');
            var queryString = string.Join("&", request.Params.Select(kv =>
                $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
            urlBuilder.Append(queryString);
        }

        return urlBuilder.ToString();
    }

    private static HttpRequestMessage CreateRequestMessage(KgRequest request, string requestUrl)
    {
        var msg = new HttpRequestMessage(request.Method, requestUrl);
        msg.Options.Set(new HttpRequestOptionsKey<KgRequest>("KgRequestDetail"), request);

        if (request.CustomHeaders != null)
            foreach (var kv in request.CustomHeaders)
                msg.Headers.TryAddWithoutValidation(kv.Key, kv.Value);

        if (request.Method != HttpMethod.Post)
            return msg;

        if (request.BinaryBody is { Length: > 0 })
        {
            msg.Content = new ByteArrayContent(request.BinaryBody);
            msg.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(request.ContentType);
        }
        else if (!string.IsNullOrEmpty(request.RawBody))
        {
            msg.Content = new StringContent(request.RawBody, Encoding.UTF8, request.ContentType);
        }
        else if (request.Body != null)
        {
            var jsonBody = RequestBodyJsonSerializer.Serialize(request.Body, request.BodyTypeInfo);
            msg.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        return msg;
    }

    private static bool IsTransient(HttpRequestException exception)
    {
        if (exception.StatusCode is null)
            return true;

        var statusCode = (int)exception.StatusCode.Value;
        return statusCode is 408 or 429 || statusCode >= 500;
    }

    private static TimeSpan GetRetryDelay(int failedAttempt)
    {
        return failedAttempt == 1
            ? TimeSpan.FromMilliseconds(250)
            : TimeSpan.FromMilliseconds(750);
    }
}
