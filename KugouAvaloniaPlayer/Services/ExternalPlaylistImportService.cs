using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.Services;

public sealed class ExternalPlaylistParseResult
{
    public bool Success { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public string SourcePlatform { get; init; } = string.Empty;
    public string SourcePlaylistName { get; init; } = string.Empty;
    public List<string> SongNames { get; init; } = new();
}

public sealed class ExternalPlaylistImportResult
{
    public bool Success => string.IsNullOrWhiteSpace(ErrorMessage);
    public string ErrorMessage { get; init; } = string.Empty;
    public int Total { get; init; }
    public int Matched { get; init; }
    public int Imported { get; init; }
    public List<string> FailedNames { get; init; } = new();
}

public sealed class ExternalPlaylistImportProgress
{
    public string Stage { get; init; } = string.Empty;
    public int Processed { get; init; }
    public int Total { get; init; }
    public double Percentage => Total <= 0 ? 0 : Math.Clamp(Processed * 100.0 / Total, 0, 100);
    public string Message { get; init; } = string.Empty;
}

public interface IExternalPlaylistParseStrategy
{
    string PlatformName { get; }
    bool CanHandle(Uri uri);

    Task<ExternalPlaylistParseResult> ParseAndLoadAsync(Uri uri, string sourceText,
        CancellationToken cancellationToken = default);
}

public interface IExternalPlaylistImportService
{
    Task<ExternalPlaylistParseResult> ParseAndLoadAsync(string sourceText,
        CancellationToken cancellationToken = default);

    Task<ExternalPlaylistImportResult> ImportToKugouAsync(
        ExternalPlaylistParseResult parseResult,
        string targetPlaylistName,
        IProgress<ExternalPlaylistImportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class ExternalPlaylistImportService(
    IEnumerable<IExternalPlaylistParseStrategy> strategies,
    MusicClient musicClient,
    PlaylistClient playlistClient,
    UserClient userClient,
    ILogger<ExternalPlaylistImportService> logger) : IExternalPlaylistImportService
{
    private const int SongBatchSize = 100;
    private static readonly Regex UrlRegex = new(@"https?://[^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly List<IExternalPlaylistParseStrategy> _strategies = strategies.ToList();

    public async Task<ExternalPlaylistParseResult> ParseAndLoadAsync(
        string sourceText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
            return new ExternalPlaylistParseResult { ErrorMessage = "链接不能为空。" };

        var url = ExtractUrl(sourceText);
        if (string.IsNullOrWhiteSpace(url))
            return new ExternalPlaylistParseResult { ErrorMessage = "未识别到有效链接。" };

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return new ExternalPlaylistParseResult { ErrorMessage = "链接格式不正确。" };

        var strategy = _strategies.FirstOrDefault(x => x.CanHandle(uri));
        if (strategy == null)
            return new ExternalPlaylistParseResult { ErrorMessage = "暂只支持网易云和QQ音乐歌单链接。" };

        return await strategy.ParseAndLoadAsync(uri, sourceText, cancellationToken);
    }

    public async Task<ExternalPlaylistImportResult> ImportToKugouAsync(
        ExternalPlaylistParseResult parseResult,
        string targetPlaylistName,
        IProgress<ExternalPlaylistImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!parseResult.Success)
            return new ExternalPlaylistImportResult { ErrorMessage = parseResult.ErrorMessage };

        if (parseResult.SongNames.Count == 0)
            return new ExternalPlaylistImportResult
            {
                ErrorMessage = "来源歌单没有可导入歌曲。"
            };

        if (string.IsNullOrWhiteSpace(targetPlaylistName))
            return new ExternalPlaylistImportResult { ErrorMessage = "目标歌单名称不能为空。" };

        try
        {
            await playlistClient.CreatePlaylistAsync(targetPlaylistName);
            var target = await WaitForCreatedPlaylistAsync(targetPlaylistName, cancellationToken);

            if (target == null)
                return new ExternalPlaylistImportResult { ErrorMessage = "创建成功，但未找到目标歌单，请稍后重试。" };

            var sourceSongNames = parseResult.SongNames.AsEnumerable().Reverse().ToList();
            var total = sourceSongNames.Count;
            var matchedSongs =
                new List<(string SourceName, string Name, string Hash, string AlbumId, string MixSongId)>();
            var failedNames = new List<string>();
            var matchedProcessed = 0;

            progress?.Report(new ExternalPlaylistImportProgress
            {
                Stage = "matching",
                Processed = 0,
                Total = total,
                Message = $"正在匹配歌曲 0/{total}"
            });

            foreach (var songName in sourceSongNames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var searchResult = await musicClient.SearchAsync(songName);
                    var first = searchResult.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Hash));
                    if (first == null)
                    {
                        failedNames.Add(songName);
                        continue;
                    }

                    matchedSongs.Add((
                        songName,
                        string.IsNullOrWhiteSpace(first.Name) ? songName : first.Name,
                        first.Hash,
                        string.IsNullOrWhiteSpace(first.AlbumId) ? "0" : first.AlbumId,
                        "0"));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "搜索歌曲失败，已跳过。 song={SongName}", songName);
                    failedNames.Add(songName);
                }

                matchedProcessed++;
                progress?.Report(new ExternalPlaylistImportProgress
                {
                    Stage = "matching",
                    Processed = matchedProcessed,
                    Total = total,
                    Message = $"正在匹配歌曲 {matchedProcessed}/{total}"
                });
            }

            var imported = 0;
            var addProcessed = 0;
            var addTotal = Math.Max(matchedSongs.Count, 1);

            progress?.Report(new ExternalPlaylistImportProgress
            {
                Stage = "adding",
                Processed = 0,
                Total = addTotal,
                Message = $"正在写入歌单 0/{matchedSongs.Count}"
            });

            foreach (var chunk in matchedSongs.Chunk(SongBatchSize))
            {
                try
                {
                    var payload = chunk
                        .Select(x => (x.Name, x.Hash, x.AlbumId, x.MixSongId))
                        .ToList();

                    var addResult = await playlistClient.AddSongsAsync(target.ListId.ToString(), payload);
                    imported += addResult?.AddedSongs.Count ?? 0;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "批量添加歌曲失败。 targetListId={ListId}", target.ListId);
                    failedNames.AddRange(chunk.Select(x => x.SourceName));
                }

                addProcessed += chunk.Length;
                progress?.Report(new ExternalPlaylistImportProgress
                {
                    Stage = "adding",
                    Processed = Math.Min(addProcessed, addTotal),
                    Total = addTotal,
                    Message = $"正在写入歌单 {Math.Min(addProcessed, matchedSongs.Count)}/{matchedSongs.Count}"
                });
            }

            return new ExternalPlaylistImportResult
            {
                Total = total,
                Matched = matchedSongs.Count,
                Imported = imported,
                FailedNames = failedNames.Distinct(StringComparer.Ordinal).ToList()
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "导入外部歌单失败。 targetPlaylist={TargetPlaylist}", targetPlaylistName);
            return new ExternalPlaylistImportResult { ErrorMessage = $"导入失败：{ex.Message}" };
        }
    }

    private static string? ExtractUrl(string text)
    {
        var match = UrlRegex.Match(text);
        return match.Success ? match.Value.Trim() : text.Trim();
    }

    private async Task<UserPlaylistItem?> WaitForCreatedPlaylistAsync(
        string targetPlaylistName,
        CancellationToken cancellationToken)
    {
        const int maxRetries = 8;
        var delay = TimeSpan.FromMilliseconds(500);

        for (var i = 0; i < maxRetries; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var playlists = await userClient.GetPlaylistsAsync();
            var target = playlists?.Playlists?
                .Where(x => !string.IsNullOrWhiteSpace(x.ListCreateId) &&
                            string.Equals(x.Name, targetPlaylistName, StringComparison.Ordinal))
                .OrderByDescending(x => x.ListId)
                .FirstOrDefault();

            if (target != null)
                return target;

            await Task.Delay(delay, cancellationToken);
        }

        return null;
    }
}

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

            using var requestContent =
                new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = playlistId });
            using var response = await client.PostAsync(NeteasePlaylistDetailApi, requestContent, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);

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

            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["c"] = BuildNeteaseSongDetailPayload(chunk)
            });
            using var resp = await client.PostAsync(NeteaseSongDetailApi, form, cancellationToken);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);

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

public sealed class QqMusicPlaylistParseStrategy(
    IHttpClientFactory httpClientFactory,
    ILogger<QqMusicPlaylistParseStrategy> logger) : IExternalPlaylistParseStrategy
{
    private const int QqPageSize = 30;
    private const int QqMaxSongs = 10000;
    private const string QqMusicApi = "https://u6.y.qq.com/cgi-bin/musics.fcg";

    public string PlatformName => "QQ音乐";

    public bool CanHandle(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        return host.Contains("y.qq.com")
               || host.Contains("qqmusic.qq.com")
               || host.Contains("music.qq.com")
               || host.Contains("c.y.qq.com");
    }

    public async Task<ExternalPlaylistParseResult> ParseAndLoadAsync(
        Uri uri,
        string sourceText,
        CancellationToken cancellationToken = default)
    {
        var resolvedUri = await ResolvePotentialRedirectAsync(uri, cancellationToken);
        if (resolvedUri == null)
            return new ExternalPlaylistParseResult { ErrorMessage = "QQ音乐链接解析失败，请稍后重试。" };

        var playlistId = ExtractQqPlaylistId(resolvedUri);
        if (playlistId <= 0)
            return new ExternalPlaylistParseResult { ErrorMessage = "未在QQ音乐链接中解析到歌单ID。" };

        try
        {
            var first = await FetchQqPlaylistPageAsync(playlistId, 0, QqPageSize, cancellationToken);
            if (first == null || (first.SongNames.Count == 0 && first.Total <= 0))
                return new ExternalPlaylistParseResult { ErrorMessage = "QQ音乐歌单数据获取失败，请稍后重试。" };

            var playlistName = string.IsNullOrWhiteSpace(first.Title) ? "导入歌单" : first.Title;
            var total = first.Total;
            if (total > QqMaxSongs)
                total = QqMaxSongs;

            var allSongs = new List<string>(Math.Max(total, first.SongNames.Count));
            allSongs.AddRange(first.SongNames);

            var pageCount = (total + QqPageSize - 1) / QqPageSize;
            for (var page = 1; page < pageCount; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var begin = page * QqPageSize;
                var num = Math.Min(QqPageSize, total - begin);
                if (num <= 0)
                    break;

                var pageData = await FetchQqPlaylistPageAsync(playlistId, begin, num, cancellationToken);
                if (pageData == null || pageData.SongNames.Count == 0)
                    continue;

                allSongs.AddRange(pageData.SongNames);
            }

            allSongs = allSongs.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList();
            if (allSongs.Count == 0)
                return new ExternalPlaylistParseResult { ErrorMessage = "QQ音乐歌单未解析到歌曲名称。" };

            return new ExternalPlaylistParseResult
            {
                Success = true,
                SourcePlatform = PlatformName,
                SourcePlaylistName = playlistName,
                SongNames = allSongs
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "加载QQ音乐歌单失败。 playlistId={PlaylistId}", playlistId);
            return new ExternalPlaylistParseResult { ErrorMessage = $"解析QQ音乐歌单失败：{ex.Message}" };
        }
    }

    private async Task<Uri?> ResolvePotentialRedirectAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!uri.AbsoluteUri.Contains("fcgi-bin", StringComparison.OrdinalIgnoreCase))
            return uri;

        try
        {
            using var client = httpClientFactory.CreateClient(nameof(QqMusicPlaylistParseStrategy));
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return resp.RequestMessage?.RequestUri ?? uri;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "解析QQ音乐短链失败。 url={Url}", uri.ToString());
            return null;
        }
    }

    private static long ExtractQqPlaylistId(Uri uri)
    {
        var full = Uri.UnescapeDataString(uri.ToString());

        var playlistPathMatch = Regex.Match(full, @"playlist/(\d+)", RegexOptions.IgnoreCase);
        if (playlistPathMatch.Success && long.TryParse(playlistPathMatch.Groups[1].Value, out var byPath))
            return byPath;

        var queryId = ExtractQueryValue(uri.Query, "id");
        if (!string.IsNullOrWhiteSpace(queryId) && long.TryParse(queryId, out var byQuery))
            return byQuery;

        var idMatch = Regex.Match(full, @"[?&]id=(\d+)", RegexOptions.IgnoreCase);
        if (idMatch.Success && long.TryParse(idMatch.Groups[1].Value, out var byRaw))
            return byRaw;

        return 0;
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

    private async Task<QqPlaylistPageData?> FetchQqPlaylistPageAsync(
        long playlistId,
        int songBegin,
        int songNum,
        CancellationToken cancellationToken)
    {
        var platforms = new[] { "-1", "android", "iphone", "h5", "wxfshare", "iphone_wx", "windows" };
        foreach (var platform in platforms)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var body = BuildQqRequestJson(playlistId, platform, songBegin, songNum);
            var sign = BuildQqSign(body);
            var url = $"{QqMusicApi}?sign={sign}&_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

            try
            {
                using var client = httpClientFactory.CreateClient(nameof(QqMusicPlaylistParseStrategy));
                using var content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
                using var resp = await client.PostAsync(url, content, cancellationToken);
                if (!resp.IsSuccessStatusCode)
                    continue;

                var text = await resp.Content.ReadAsStringAsync(cancellationToken);
                var parsed = ParseQqPageData(text);
                if (parsed != null)
                    return parsed;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "请求QQ歌单页面失败。 platform={Platform}, begin={Begin}", platform, songBegin);
            }
        }

        return null;
    }

    private static QqPlaylistPageData? ParseQqPageData(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number &&
            codeEl.GetInt32() != 0)
            return null;
        if (!root.TryGetProperty("req_0", out var req0))
            return null;
        if (!req0.TryGetProperty("data", out var data))
            return null;

        var title = "";
        var total = 0;
        if (data.TryGetProperty("dirinfo", out var dirinfo))
        {
            if (dirinfo.TryGetProperty("title", out var titleEl))
                title = titleEl.GetString() ?? "";
            if (dirinfo.TryGetProperty("songnum", out var numEl) && numEl.ValueKind == JsonValueKind.Number)
                total = numEl.GetInt32();
        }

        var songs = new List<string>();
        if (data.TryGetProperty("songlist", out var listEl) && listEl.ValueKind == JsonValueKind.Array)
            foreach (var songEl in listEl.EnumerateArray())
                if (songEl.TryGetProperty("name", out var nameEl))
                {
                    var name = nameEl.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                        songs.Add(name);
                }

        return new QqPlaylistPageData
        {
            Title = title,
            Total = total,
            SongNames = songs
        };
    }

    private static string BuildQqRequestJson(long playlistId, string platform, int songBegin, int songNum)
    {
        return
            $"{{\"req_0\":{{\"module\":\"music.srfDissInfo.aiDissInfo\",\"method\":\"uniform_get_Dissinfo\",\"param\":{{\"disstid\":{playlistId},\"enc_host_uin\":\"\",\"tag\":1,\"userinfo\":1,\"song_begin\":{songBegin},\"song_num\":{songNum}}}}},\"comm\":{{\"g_tk\":5381,\"uin\":0,\"format\":\"json\",\"platform\":\"{platform}\"}}}}";
    }

    private static string BuildQqSign(string param)
    {
        var l1 = new[] { 212, 45, 80, 68, 195, 163, 163, 203, 157, 220, 254, 91, 204, 79, 104, 6 };
        const string t = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=";

        var md5 = MD5.HashData(Encoding.UTF8.GetBytes(param));
        var md5Str = Convert.ToHexString(md5).ToUpperInvariant();

        var t1 = SelectChars(md5Str, new[] { 21, 4, 9, 26, 16, 20, 27, 30 });
        var t3 = SelectChars(md5Str, new[] { 18, 11, 3, 2, 1, 7, 6, 25 });

        var ls2 = new List<int>(16);
        for (var i = 0; i < 16; i++)
        {
            var x1 = HexValue(md5Str[i * 2]);
            var x2 = HexValue(md5Str[i * 2 + 1]);
            var x3 = (x1 * 16) ^ x2 ^ l1[i];
            ls2.Add(x3);
        }

        var ls3 = new StringBuilder();
        for (var i = 0; i < 6; i++)
            if (i == 5)
            {
                ls3.Append(t[ls2[^1] >> 2]);
                ls3.Append(t[(ls2[^1] & 3) << 4]);
            }
            else
            {
                var x4 = ls2[i * 3] >> 2;
                var x5 = (ls2[i * 3 + 1] >> 4) ^ ((ls2[i * 3] & 3) << 4);
                var x6 = (ls2[i * 3 + 2] >> 6) ^ ((ls2[i * 3 + 1] & 15) << 2);
                var x7 = 63 & ls2[i * 3 + 2];
                ls3.Append(t[x4]);
                ls3.Append(t[x5]);
                ls3.Append(t[x6]);
                ls3.Append(t[x7]);
            }

        var t2 = ls3.ToString().Replace("[\\/+]", "", StringComparison.Ordinal);
        return "zzb" + (t1 + t2 + t3).ToLowerInvariant();
    }

    private static int HexValue(char c)
    {
        if (c is >= '0' and <= '9') return c - '0';
        if (c is >= 'A' and <= 'F') return c - 'A' + 10;
        return c - 'a' + 10;
    }

    private static string SelectChars(string source, int[] indexes)
    {
        var sb = new StringBuilder(indexes.Length);
        foreach (var idx in indexes)
            sb.Append(source[idx]);
        return sb.ToString();
    }

    private sealed class QqPlaylistPageData
    {
        public string Title { get; init; } = string.Empty;
        public int Total { get; init; }
        public List<string> SongNames { get; init; } = new();
    }
}