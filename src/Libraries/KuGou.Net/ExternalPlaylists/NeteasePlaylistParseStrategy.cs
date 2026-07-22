using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace KuGou.Net.ExternalPlaylists;

public sealed class NeteasePlaylistParseStrategy(
    IHttpClientFactory httpClientFactory,
    ILogger<NeteasePlaylistParseStrategy> logger) : IExternalPlaylistParseStrategy
{
    private const int NeteaseSongDetailChunkSize = 400;
    private const string NeteasePlaylistDetailApi = "https://music.163.com/api/v6/playlist/detail";
    private const string NeteaseSongDetailApi = "https://music.163.com/api/v3/song/detail";

    public string PlatformName => "网易云";

    public bool CanHandle(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        return host.EndsWith("music.163.com")
               || host == "y.music.163.com"
               || host == "163cn.tv";
    }

    public async Task<ExternalPlaylistParseResult> ParseAndLoadAsync(
        Uri uri,
        string sourceText,
        CancellationToken cancellationToken = default)
    {
        var resolvedUri = await ResolveIfShortLinkAsync(uri, cancellationToken);
        if (resolvedUri == null)
            return new ExternalPlaylistParseResult { ErrorMessage = "网易云短链解析失败，请稍后重试。" };

        var playlistId = ExtractPlaylistId(resolvedUri);
        if (string.IsNullOrWhiteSpace(playlistId))
            return new ExternalPlaylistParseResult { ErrorMessage = "未在网易云链接中解析到歌单ID。" };

        try
        {
            using var client = httpClientFactory.CreateClient(nameof(NeteasePlaylistParseStrategy));
            client.DefaultRequestHeaders.Referrer = new Uri("https://music.163.com/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0 Safari/537.36");

            using var request = new HttpRequestMessage(HttpMethod.Post, NeteasePlaylistDetailApi)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = playlistId })
            };
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("playlist", out var playlist))
                return new ExternalPlaylistParseResult { ErrorMessage = "网易云响应格式异常，未找到歌单信息。" };

            var sourcePlaylistName = playlist.TryGetProperty("name", out var playlistNameEl)
                ? playlistNameEl.GetString() ?? "导入歌单"
                : "导入歌单";

            var trackIds = new List<long>();
            if (playlist.TryGetProperty("trackIds", out var trackIdsEl) && trackIdsEl.ValueKind == JsonValueKind.Array)
                foreach (var track in trackIdsEl.EnumerateArray())
                    if (track.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out var id))
                        trackIds.Add(id);

            var songs = trackIds.Count > 0
                ? await LoadSongNamesByTrackIdsAsync(client, trackIds, cancellationToken)
                : LoadSongNamesFromTracksFallback(playlist);

            songs = songs.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList();
            if (songs.Count == 0)
                return new ExternalPlaylistParseResult { ErrorMessage = "网易云歌单未解析到歌曲名称，可能是私密歌单或接口受限。" };

            return new ExternalPlaylistParseResult
            {
                Success = true,
                SourcePlatform = PlatformName,
                SourcePlaylistName = sourcePlaylistName,
                SongNames = songs
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "加载网易云歌单失败。 playlistId={PlaylistId}", playlistId);
            return new ExternalPlaylistParseResult { ErrorMessage = $"解析网易云歌单失败：{ex.Message}" };
        }
    }

    private async Task<Uri?> ResolveIfShortLinkAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!string.Equals(uri.Host, "163cn.tv", StringComparison.OrdinalIgnoreCase))
            return uri;

        try
        {
            using var client = httpClientFactory.CreateClient(nameof(NeteasePlaylistParseStrategy));
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return resp.RequestMessage?.RequestUri ?? uri;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "解析网易云短链失败。 url={Url}", uri.ToString());
            return null;
        }
    }

    private static string? ExtractPlaylistId(Uri uri)
    {
        var queryId = ExtractQueryValue(uri.Query, "id");
        if (!string.IsNullOrWhiteSpace(queryId))
            return queryId;

        var fragment = uri.Fragment.TrimStart('#');
        if (fragment.StartsWith('/'))
            fragment = fragment[1..];

        var fragmentQueryIndex = fragment.IndexOf('?', StringComparison.Ordinal);
        if (fragmentQueryIndex >= 0)
        {
            var fragmentQuery = fragment[(fragmentQueryIndex + 1)..];
            var fragmentId = ExtractQueryValue(fragmentQuery, "id");
            if (!string.IsNullOrWhiteSpace(fragmentId))
                return fragmentId;
        }

        var full = Uri.UnescapeDataString(uri.ToString());
        var regex = Regex.Match(full, @"(?:playlist|songlist)\?id=(\d+)", RegexOptions.IgnoreCase);
        return regex.Success ? regex.Groups[1].Value : null;
    }

    private static string? ExtractQueryValue(string query, string key)
    {
        var q = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(q))
            return null;

        foreach (var segment in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = segment.IndexOf('=', StringComparison.Ordinal);
            if (index <= 0)
                continue;

            var currentKey = segment[..index];
            if (!string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
                continue;

            var value = segment[(index + 1)..];
            return string.IsNullOrWhiteSpace(value) ? null : Uri.UnescapeDataString(value);
        }

        return null;
    }

    private static List<string> LoadSongNamesFromTracksFallback(JsonElement playlist)
    {
        var songs = new List<string>();
        if (playlist.TryGetProperty("tracks", out var tracksEl) && tracksEl.ValueKind == JsonValueKind.Array)
            foreach (var track in tracksEl.EnumerateArray())
                if (track.TryGetProperty("name", out var songNameEl))
                {
                    var name = songNameEl.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                        songs.Add(name);
                }

        return songs;
    }

    private async Task<List<string>> LoadSongNamesByTrackIdsAsync(
        HttpClient client,
        List<long> trackIds,
        CancellationToken cancellationToken)
    {
        var songs = new List<string>(trackIds.Count);

        foreach (var chunk in trackIds.Chunk(NeteaseSongDetailChunkSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var request = new HttpRequestMessage(HttpMethod.Post, NeteaseSongDetailApi)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["c"] = BuildNeteaseSongDetailPayload(chunk)
                })
            };
            using var resp = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            resp.EnsureSuccessStatusCode();

            await using var responseStream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("songs", out var songsEl) || songsEl.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var song in songsEl.EnumerateArray())
                if (song.TryGetProperty("name", out var nameEl))
                {
                    var name = nameEl.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                        songs.Add(name);
                }
        }

        return songs;
    }

    private static string BuildNeteaseSongDetailPayload(long[] ids)
    {
        var sb = new StringBuilder();
        sb.Append('[');
        for (var i = 0; i < ids.Length; i++)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append("{\"id\":");
            sb.Append(ids[i]);
            sb.Append('}');
        }

        sb.Append(']');
        return sb.ToString();
    }
}
