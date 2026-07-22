using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace KuGou.Net.ExternalPlaylists;

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
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded")
                };
                using var resp = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
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
        if (root.TryGetProperty("code", out var codeEl)
            && codeEl.ValueKind == JsonValueKind.Number
            && codeEl.GetInt32() != 0)
            return null;

        if (!root.TryGetProperty("req_0", out var req0) || !req0.TryGetProperty("data", out var data))
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
        if (c is >= '0' and <= '9')
            return c - '0';
        if (c is >= 'A' and <= 'F')
            return c - 'A' + 10;
        return c - 'a' + 10;
    }

    private static string SelectChars(string source, int[] indexes)
    {
        var sb = new StringBuilder(indexes.Length);
        foreach (var index in indexes)
            sb.Append(source[index]);
        return sb.ToString();
    }

    private sealed class QqPlaylistPageData
    {
        public string Title { get; init; } = string.Empty;
        public int Total { get; init; }
        public List<string> SongNames { get; init; } = new();
    }
}
