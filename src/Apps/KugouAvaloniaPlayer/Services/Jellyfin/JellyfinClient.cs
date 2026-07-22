using System;
using System.Collections.Generic;
using System.Globalization;
using ZLinq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using KugouAvaloniaPlayer.Models;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.Services.Jellyfin;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(JellyfinItemsResponse))]
[JsonSerializable(typeof(JellyfinBaseItemDto))]
internal partial class JellyfinJsonContext : JsonSerializerContext
{
}

public interface IJellyfinClient
{
    Task<IReadOnlyList<JellyfinLibrary>> GetMusicLibrariesAsync(
        JellyfinConnectionOptions options,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JellyfinAudioItem>> GetAudioItemsAsync(
        JellyfinConnectionOptions options,
        string libraryId,
        IProgress<JellyfinImportProgress>? progress = null,
        CancellationToken cancellationToken = default);

    string GetServerFingerprint(string serverUrl);
    string BuildPseudoPath(string serverFingerprint, string itemId);
    string? BuildStreamUrlFromPseudoPath(string pseudoPath);
}

public sealed class JellyfinClient(IHttpClientFactory httpClientFactory, ILogger<JellyfinClient> logger)
    : IJellyfinClient
{
    private const int PageSize = 200;
    private const string SourceScheme = "jellyfin://";

    public async Task<IReadOnlyList<JellyfinLibrary>> GetMusicLibrariesAsync(
        JellyfinConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeOptions(options);
        using var request = CreateRequest(normalized, HttpMethod.Get, $"/Users/{Uri.EscapeDataString(normalized.UserId)}/Views");
        using var response = await SendAsync(request, cancellationToken);
        var payload = await ReadItemsResponseAsync(response, cancellationToken);

        return payload.Items
            .AsValueEnumerable().Where(x => string.Equals(x.CollectionType, "music", StringComparison.OrdinalIgnoreCase))
            .Select(x => new JellyfinLibrary(x.Id ?? string.Empty, x.Name ?? "Jellyfin 音乐库"))
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToList();
    }

    public async Task<IReadOnlyList<JellyfinAudioItem>> GetAudioItemsAsync(
        JellyfinConnectionOptions options,
        string libraryId,
        IProgress<JellyfinImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeOptions(options);
        var items = new List<JellyfinAudioItem>();
        var startIndex = 0;
        var total = 0;

        do
        {
            var query = string.Join("&",
                $"ParentId={Uri.EscapeDataString(libraryId)}",
                "Recursive=true",
                "IncludeItemTypes=Audio",
                "Fields=BasicSyncInfo,MediaSources,Genres",
                "SortBy=SortName",
                "SortOrder=Ascending",
                $"StartIndex={startIndex.ToString(CultureInfo.InvariantCulture)}",
                $"Limit={PageSize.ToString(CultureInfo.InvariantCulture)}");

            using var request = CreateRequest(
                normalized,
                HttpMethod.Get,
                $"/Users/{Uri.EscapeDataString(normalized.UserId)}/Items?{query}");
            using var response = await SendAsync(request, cancellationToken);
            var payload = await ReadItemsResponseAsync(response, cancellationToken);

            total = payload.TotalRecordCount;
            foreach (var item in payload.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Id))
                    continue;

                items.Add(new JellyfinAudioItem(
                    item.Id,
                    string.IsNullOrWhiteSpace(item.Name) ? "未知歌曲" : item.Name!,
                    ResolveArtist(item),
                    item.AlbumId ?? string.Empty,
                    item.Album ?? string.Empty,
                    (item.RunTimeTicks ?? 0) / 10_000_000.0,
                    BuildImageUrl(normalized, string.IsNullOrWhiteSpace(item.AlbumId) ? item.Id : item.AlbumId!),
                    BuildStreamUrl(normalized, item.Id)));
            }

            startIndex += payload.Items.Count;
            progress?.Report(new JellyfinImportProgress
            {
                Processed = items.Count,
                Total = Math.Max(total, items.Count),
                Message = $"正在读取 Jellyfin 曲目 {items.Count}/{Math.Max(total, items.Count)}"
            });
        } while (startIndex < total && total > 0);

        return items;
    }

    public string GetServerFingerprint(string serverUrl)
    {
        var normalized = NormalizeServerUrl(serverUrl);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    public string BuildPseudoPath(string serverFingerprint, string itemId)
    {
        return $"{SourceScheme}{serverFingerprint}/{itemId}";
    }

    public string? BuildStreamUrlFromPseudoPath(string pseudoPath)
    {
        if (!TryParsePseudoPath(pseudoPath, out var fingerprint, out var itemId))
            return null;

        if (!SettingsManager.Settings.JellyfinServers.TryGetValue(fingerprint, out var settings))
            return null;

        var options = new JellyfinConnectionOptions(settings.ServerUrl, settings.UserId, settings.ApiKey);
        try
        {
            var normalized = NormalizeOptions(options);
            return BuildStreamUrl(normalized, itemId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "构建 Jellyfin 播放地址失败。 fingerprint={Fingerprint}", fingerprint);
            return null;
        }
    }

    private static string BuildImageUrl(JellyfinConnectionOptions options, string itemId)
    {
        return $"{options.ServerUrl}/Items/{Uri.EscapeDataString(itemId)}/Images/Primary?fillWidth=512&fillHeight=512&quality=90&api_key={Uri.EscapeDataString(options.ApiKey)}";
    }

    private static string BuildStreamUrl(JellyfinConnectionOptions options, string itemId)
    {
        return $"{options.ServerUrl}/Audio/{Uri.EscapeDataString(itemId)}/stream?static=true&api_key={Uri.EscapeDataString(options.ApiKey)}";
    }

    private HttpRequestMessage CreateRequest(JellyfinConnectionOptions options, HttpMethod method, string pathAndQuery)
    {
        var request = new HttpRequestMessage(method, options.ServerUrl + pathAndQuery);
        request.Headers.TryAddWithoutValidation("X-Emby-Token", options.ApiKey);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("KugouAvaloniaPlayer", "1.0"));
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(nameof(JellyfinClient));
        var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Jellyfin 请求失败：{(int)response.StatusCode} {response.ReasonPhrase} {body}");
        }

        return response;
    }

    private static async Task<JellyfinItemsResponse> ReadItemsResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync(stream, JellyfinJsonContext.Default.JellyfinItemsResponse, cancellationToken)
               ?? new JellyfinItemsResponse();
    }

    private static JellyfinConnectionOptions NormalizeOptions(JellyfinConnectionOptions options)
    {
        var serverUrl = NormalizeServerUrl(options.ServerUrl);
        if (string.IsNullOrWhiteSpace(options.UserId))
            throw new InvalidOperationException("Jellyfin UserId 不能为空。");
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException("Jellyfin API Key 不能为空。");

        return new JellyfinConnectionOptions(serverUrl, options.UserId.Trim(), options.ApiKey.Trim());
    }

    private static string NormalizeServerUrl(string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
            throw new InvalidOperationException("Jellyfin 服务器地址不能为空。");

        var normalized = serverUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Jellyfin 服务器地址必须是 http 或 https URL。");
        }

        return normalized;
    }

    private static string ResolveArtist(JellyfinBaseItemDto item)
    {
        if (item.Artists is { Count: > 0 })
            return string.Join(", ", item.Artists.AsValueEnumerable().Where(x => !string.IsNullOrWhiteSpace(x)).ToArray());

        return string.IsNullOrWhiteSpace(item.AlbumArtist) ? "未知艺术家" : item.AlbumArtist!;
    }

    private static bool TryParsePseudoPath(string pseudoPath, out string fingerprint, out string itemId)
    {
        fingerprint = string.Empty;
        itemId = string.Empty;

        if (string.IsNullOrWhiteSpace(pseudoPath) ||
            !pseudoPath.StartsWith(SourceScheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = pseudoPath[SourceScheme.Length..];
        var slashIndex = rest.IndexOf('/');
        if (slashIndex <= 0 || slashIndex >= rest.Length - 1)
            return false;

        fingerprint = rest[..slashIndex];
        itemId = rest[(slashIndex + 1)..];
        return true;
    }
}

internal sealed class JellyfinItemsResponse
{
    public List<JellyfinBaseItemDto> Items { get; set; } = new();
    public int TotalRecordCount { get; set; }
}

internal sealed class JellyfinBaseItemDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? CollectionType { get; set; }
    public string? Album { get; set; }
    public string? AlbumId { get; set; }
    public string? AlbumArtist { get; set; }
    public List<string>? Artists { get; set; }
    public long? RunTimeTicks { get; set; }
}
