using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using KuGou.Net.Abstractions.Models;

namespace KuGou.Net.Native;

public static partial class NativeExports
{
    private const string PendingJson = "{\"state\":\"pending\"}";
    private static readonly ConcurrentDictionary<long, Task<string>> PendingRequests = new();
    private static long _nextRequestId;

    /// <summary>
    /// Starts an SDK request without waiting for network I/O. The returned positive id can be
    /// passed to KgRequestPoll. Parse and initialization failures are represented by an already
    /// completed request, so managed exceptions never cross the C ABI boundary.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "KgRequestStart")]
    public static long KgRequestStart(IntPtr requestJsonPtr)
    {
        var requestId = Interlocked.Increment(ref _nextRequestId);
        try
        {
            var requestJson = GetStr(requestJsonPtr);
            var request = JsonSerializer.Deserialize(requestJson, NativeJsonContext.Default.NativeRequest)
                          ?? throw new JsonException("Request JSON cannot be null.");

            EnsureInitialized();
            ApplySessionCredentials(request.Session);
            PendingRequests[requestId] = ExecuteRequestAsync(request);
            return requestId;
        }
        catch (Exception ex)
        {
            PendingRequests[requestId] = Task.FromResult(Failed(ex));
            return requestId;
        }
    }

    /// <summary>
    /// Returns {state:"pending"} while a request is running and removes/returns its final result
    /// once complete. Every returned pointer must be released with KgFreeMemory.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "KgRequestPoll")]
    public static IntPtr KgRequestPoll(long requestId)
    {
        try
        {
            if (!PendingRequests.TryGetValue(requestId, out var requestTask))
                return Marshal.StringToCoTaskMemUTF8(Failed("Unknown or already consumed request id.", 404));

            if (!requestTask.IsCompleted)
                return Marshal.StringToCoTaskMemUTF8(PendingJson);

            PendingRequests.TryRemove(requestId, out _);
            // IsCompleted was checked above and ExecuteRequestAsync captures all exceptions.
            return Marshal.StringToCoTaskMemUTF8(requestTask.GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            PendingRequests.TryRemove(requestId, out _);
            return Marshal.StringToCoTaskMemUTF8(Failed(ex));
        }
    }

    /// <summary>
    /// Stops tracking a request. The current SDK clients don't expose CancellationToken yet, so
    /// this prevents result delivery but doesn't forcibly abort an in-flight HTTP operation.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "KgRequestCancel")]
    public static int KgRequestCancel(long requestId)
    {
        try
        {
            return PendingRequests.TryRemove(requestId, out _) ? 1 : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static async Task<string> ExecuteRequestAsync(NativeRequest request)
    {
        try
        {
            var payload = await DispatchAsync(request).ConfigureAwait(false);
            return $"{{\"state\":\"completed\",\"statusCode\":200,\"data\":{payload}}}";
        }
        catch (NotSupportedException ex)
        {
            return Failed(ex.Message, 501);
        }
        catch (ArgumentException ex)
        {
            return Failed(ex.Message, 400);
        }
        catch (Exception ex)
        {
            return Failed(ex);
        }
    }

    private static void ApplySessionCredentials(NativeSessionCredentials? credentials)
    {
        var manager = _sessionManager!;
        var current = manager.Session;
        var userId = string.IsNullOrWhiteSpace(credentials?.UserId) ? "0" : credentials.UserId;
        var token = credentials?.Token ?? string.Empty;
        var t1 = credentials?.T1 ?? string.Empty;

        if (current.UserId == userId && current.Token == token && current.T1 == t1) return;

        manager.UpdateAuth(userId, token, current.VipType, current.VipToken, t1);
    }

    private static async Task<string> DispatchAsync(NativeRequest request)
    {
        var path = NormalizePath(request.Path);

        return path switch
        {
            "/captcha/sent" => Serialize(await _loginClient!.SendCodeAsync(Required(request, "mobile")),
                NativeJsonContext.Default.SendCodeResponse),
            "/login/cellphone" => await LoginByMobileAsync(request),
            "/login/token" => Serialize(await _loginClient!.RefreshSessionAsync(),
                NativeJsonContext.Default.RefreshTokenResponse),
            "/login/logout" => LogOut(),
            "/login/qr/key" => Serialize(await _loginClient!.GetQrCodeAsync(), NativeJsonContext.Default.QRCode),
            "/login/qr/check" => Serialize(await _loginClient!.CheckQrStatusAsync(Required(request, "key")),
                NativeJsonContext.Default.QrLoginStatusResponse),

            "/user/detail" => Serialize(await _userClient!.GetUserInfoAsync(),
                NativeJsonContext.Default.UserDetailModel),
            "/user/playlist" => Serialize(await _userClient!.GetPlaylistsAsync(Int(request, "page", 1),
                Int(request, "pagesize", 30)), NativeJsonContext.Default.UserPlaylistResponse),
            "/user/vip/detail" => Serialize(await _userClient!.GetVipInfoAsync(),
                NativeJsonContext.Default.UserVipResponse),
            "/user/cloud" => Serialize(await _userClient!.GetCloudAsync(Int(request, "page", 1),
                Int(request, "pagesize", 30)), NativeJsonContext.Default.UserCloudResponse),
            "/user/cloud/url" => Serialize(await _userClient!.GetCloudUrlAsync(Required(request, "hash"),
                String(request, "album_audio_id"), String(request, "audio_id"), String(request, "name")),
                NativeJsonContext.Default.UserCloudUrlResponse),

            "/top/playlist" => Serialize(await _recommendClient!.GetRecommendedPlaylistsAsync(
                    Int(request, "category_id"), Int(request, "page", 1), Int(request, "pagesize", 30)),
                NativeJsonContext.Default.RecommendPlaylistResponse),
            "/recommend/songs" => Serialize(await _recommendClient!.GetRecommendedSongsAsync(),
                NativeJsonContext.Default.DailyRecommendResponse),
            "/personal/fm" => Serialize(await _recommendClient!.GetPersonalRecommendFMAsync(
                    String(request, "hash"), String(request, "songid"), NullableInt(request, "playtime"),
                    String(request, "action") ?? "play", String(request, "mode") ?? "normal",
                    Int(request, "songPoolId"), Bool(request, "isOverplay"), Int(request, "remainSongCnt")),
                NativeJsonContext.Default.PersonalFmResponse),
            "/top/song" => Raw(await _recommendClient!.GetNewSongsAsync(Int(request, "type", 21608),
                Int(request, "page", 1), Int(request, "pagesize", 30))),
            "/top/album" => Raw(await _recommendClient!.GetTopAlbumsAsync(Int(request, "page", 1),
                Int(request, "pagesize", 30))),
            "/top/card" => Serialize(await _recommendClient!.GetTopCardAsync(Int(request, "card_id", 1)),
                NativeJsonContext.Default.TopCardResponse),

            "/album/shop" => Raw(await _albumClient!.GetAlbumShopAsync()),
            "/album/songs" => Serialize(await _albumClient!.GetSongsAsync(Required(request, "id"),
                Int(request, "page", 1), Int(request, "pagesize", 30)),
                NativeJsonContext.Default.ListAlbumSongItem),
            "/artist/detail" => Serialize(await _artistClient!.GetDetailAsync(Required(request, "id")),
                NativeJsonContext.Default.SingerDetailResponse),
            "/artist/audios" => Serialize(await _artistClient!.GetAudiosAsync(Required(request, "id"),
                    Int(request, "page", 1), Int(request, "pagesize", 30), String(request, "sort") ?? "new"),
                NativeJsonContext.Default.SingerAudioResponse),
            "/artist/albums" => Serialize(await _artistClient!.GetAlbumsAsync(Required(request, "id"),
                    Int(request, "page", 1), Int(request, "pagesize", 30), String(request, "sort") ?? "new"),
                NativeJsonContext.Default.ArtistAlbumResponse),

            "/fm/recommend" => Serialize(await _fmClient!.GetRecommendAsync(),
                NativeJsonContext.Default.FmRecommendResponse),
            "/fm/class" => Raw(await _fmClient!.GetClassSongAsync()),
            "/fm/songs" => Serialize(await _fmClient!.GetSongsAsync(Required(request, "fmid"),
                    Int(request, "type", 2), Int(request, "offset", -1), Int(request, "size", 20)),
                NativeJsonContext.Default.FmSongResponse),
            "/fm/image" => Serialize(await _fmClient!.GetImagesAsync(Required(request, "fmid")),
                NativeJsonContext.Default.FmImageResponse),

            "/comment/music" => Serialize(await _commentClient!.GetMusicCommentsAsync(
                    Required(request, "mixsongid"), Int(request, "page", 1), Int(request, "pagesize", 30)),
                NativeJsonContext.Default.MusicCommentResponse),

            "/playlist/detail" => Serialize(await _playlistClient!.GetInfoAsync(Required(request, "ids")),
                NativeJsonContext.Default.PlaylistInfo),
            "/playlist/track/all" => Serialize(await _playlistClient!.GetSongsAsync(Required(request, "id"),
                    Int(request, "page", 1), Int(request, "pagesize", 30)),
                NativeJsonContext.Default.PlaylistSongResponse),
            "/playlist/similar" => Raw(await _playlistClient!.GetSimilarRawAsync(Required(request, "ids"))),
            "/playlist/create" => RawNullable(await _playlistClient!.CreatePlaylistAsync(
                Required(request, "name"), Long(request, "type"))),
            "/playlist/add" => RawNullable(await _playlistClient!.CollectPlaylistAsync(
                Required(request, "name"), Required(request, "list_create_gid"))),
            "/playlist/del" => RawNullable(await _playlistClient!.DeletePlaylistAsync(Required(request, "listid"))),
            "/playlist/tracks/add" => await AddTracksAsync(request),
            "/playlist/tracks/del" => Serialize(await _playlistClient!.RemoveSongsAsync(
                    Required(request, "listid"), LongList(request, "fileids")),
                NativeJsonContext.Default.RemoveSongResponse),

            "/song/climax" => Raw(await _songClient!.GetSongClimaxAsync(Required(request, "hash"))),
            "/song/url" => Serialize(await _songClient!.GetPlayInfoAsync(Required(request, "hash"),
                    String(request, "quality") ?? "128", String(request, "album_id"),
                    String(request, "album_audio_id"), Bool(request, "free_part")),
                NativeJsonContext.Default.PlayUrlData),

            "/search" => Serialize(await _searchClient!.SearchAsync(Required(request, "keywords"),
                    Int(request, "page", 1), String(request, "type") ?? "song", Int(request, "pagesize", 30)),
                NativeJsonContext.Default.ListSongInfo),
            "/search/hot" => Serialize(await _searchClient!.GetSearchHotAsync(),
                NativeJsonContext.Default.SearchHotResponse),
            "/search/suggest" => Raw(await _searchClient!.SearchSuggestRawAsync(Required(request, "keywords"))),
            "/search/lyric" => Raw(await _lyricClient!.SearchLyricAsync(String(request, "hash"),
                String(request, "album_audio_id"), String(request, "keyword"), String(request, "man"))),
            "/lyric" => Serialize(await _lyricClient!.GetLyricAsync(Required(request, "id"),
                    Required(request, "accesskey"), String(request, "fmt") ?? "krc", Bool(request, "decode")),
                NativeJsonContext.Default.LyricResult),

            "/lastest/songs/listen" => RawNullable(await _reportClient!.GetLatestSongsAsync(
                Int(request, "pagesize", 30))),
            "/listen/timeadd" => RawNullable(await _reportClient!.AddListenTimeAsync()),
            "/youth/month/vip/record" => Serialize(await _userClient!.GetVipRecordAsync(),
                NativeJsonContext.Default.VipReceiveHistoryResponse),
            "/youth/day/vip" => Serialize(await _userClient!.ReceiveOneDayVipAsync(),
                NativeJsonContext.Default.OneDayVipModel),
            "/youth/day/vip/upgrade" => Serialize(await _userClient!.UpgradeVipRewardAsync(),
                NativeJsonContext.Default.UpgradeVipModel),

            // These are application-server features rather than KuGou SDK features. Flutter can
            // keep using HTTP for them while all music API traffic moves in-process.
            "/mobile/app/versions/latest" or "/playlist/external/parse" =>
                throw new NotSupportedException($"{path} is owned by the application server and has no native SDK implementation."),
            _ => throw new NotSupportedException($"Native route is not implemented: {path}")
        };
    }

    private static async Task<string> LoginByMobileAsync(NativeRequest request)
    {
        var body = Body<NativeLoginRequest>(request, NativeJsonContext.Default.NativeLoginRequest);
        var response = await _loginClient!.LoginByMobileAsync(body.Mobile, body.Code, body.UserId);
        if (response?.RequiresUserSelection == true)
        {
            var selection = new NativeLoginAccountSelection(
                response.Status ?? 0,
                response.ErrorCode ?? 34175,
                true,
                response.FailureMessage ?? "请选择需要登录的账号",
                response.Data!.InfoList.Select(account => new NativeLoginAccount(
                    account.UserId, account.Nickname, account.Pic, account.AppId, account.Username)).ToArray());
            return Serialize(selection, NativeJsonContext.Default.NativeLoginAccountSelection);
        }

        return Serialize(response, NativeJsonContext.Default.LoginResponse);
    }

    private static async Task<string> AddTracksAsync(NativeRequest request)
    {
        var body = Body<NativeAddTracksRequest>(request, NativeJsonContext.Default.NativeAddTracksRequest);
        var songs = body.Songs.Select(song => (
            Name: song.Name,
            Hash: song.Hash,
            AlbumId: string.IsNullOrEmpty(song.AlbumId) ? "0" : song.AlbumId,
            MixSongId: string.IsNullOrEmpty(song.MixSongId) ? "0" : song.MixSongId)).ToList();
        var result = await _playlistClient!.AddSongsAsync(body.ListId, songs);
        return Serialize(result, NativeJsonContext.Default.AddSongResponse);
    }

    private static string LogOut()
    {
        _loginClient!.LogOutAsync();
        return "null";
    }

    private static T Body<T>(NativeRequest request, JsonTypeInfo<T> typeInfo)
    {
        if (request.Body is not { } body)
            throw new ArgumentException("Request body is required.");
        return body.Deserialize(typeInfo) ?? throw new ArgumentException("Request body is invalid.");
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Request path is required.");
        var normalized = path.Trim();
        return normalized.StartsWith('/') ? normalized : "/" + normalized;
    }

    private static JsonElement? Value(NativeRequest request, string key)
    {
        if (request.Query is null) return null;
        return request.Query.TryGetValue(key, out var value) ? value : null;
    }

    private static string? String(NativeRequest request, string key)
    {
        var value = Value(request, key);
        if (value is null or { ValueKind: JsonValueKind.Null }) return null;
        return value.Value.ValueKind == JsonValueKind.String
            ? value.Value.GetString()
            : value.Value.ToString();
    }

    private static string Required(NativeRequest request, string key)
    {
        var value = String(request, key);
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Query parameter '{key}' is required.")
            : value;
    }

    private static int Int(NativeRequest request, string key, int fallback = 0)
        => int.TryParse(String(request, key), out var value) ? value : fallback;

    private static int? NullableInt(NativeRequest request, string key)
        => int.TryParse(String(request, key), out var value) ? value : null;

    private static long Long(NativeRequest request, string key, long fallback = 0)
        => long.TryParse(String(request, key), out var value) ? value : fallback;

    private static bool Bool(NativeRequest request, string key, bool fallback = false)
        => bool.TryParse(String(request, key), out var value) ? value : fallback;

    private static IEnumerable<long> LongList(NativeRequest request, string key)
        => Required(request, key).Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => long.TryParse(value, out var parsed) ? parsed : 0)
            .Where(value => value != 0);

    private static string Serialize<T>(T value, JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Serialize(value, typeInfo);

    private static string Raw(JsonElement value) => value.GetRawText();
    private static string RawNullable(JsonElement? value) => value?.GetRawText() ?? "null";

    private static string Failed(Exception exception, int statusCode = 500)
        => Failed(exception.Message, statusCode);

    private static string Failed(string message, int statusCode)
        => $"{{\"state\":\"failed\",\"statusCode\":{statusCode},\"error\":{JsonSerializer.Serialize(message, NativeJsonContext.Default.String)}}}";
}
