using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using KuGou.Net.Clients;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using Microsoft.Extensions.Logging.Abstractions;

namespace KuGou.Net.Native;

public static class NativeExports
{
    private static KgSessionManager? _sessionManager;
    private static LoginClient? _loginClient;
    private static RegisterClient? _registerClient;
    private static RecommendClient? _recommendClient;
    private static SearchClient? _searchClient;
    private static SongClient? _songClient;
    private static ArtistClient? _artistClient;
    private static PlaylistClient? _playlistClient;
    private static RankClient? _rankClient;
    private static UserClient? _userClient;
    private static LyricClient? _lyricClient;
    private static AlbumClient? _albumClient;

    #region 工具方法 (处理指针与序列化)

    private static string GetStr(IntPtr ptr)
    {
        return ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
    }

    private static IntPtr ToJsonPtr<T>(T? obj, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            if (obj == null) return Marshal.StringToCoTaskMemUTF8("null");
            var json = JsonSerializer.Serialize(obj, typeInfo);
            return Marshal.StringToCoTaskMemUTF8(json);
        }
        catch (Exception ex)
        {
            return ReturnError(ex.Message);
        }
    }

    private static IntPtr ReturnError(string msg)
    {
        var json = JsonSerializer.Serialize(new NativeErrorResult(msg), NativeJsonContext.Default.NativeErrorResult);
        return Marshal.StringToCoTaskMemUTF8(json);
    }

    private static IntPtr ReturnBool(bool res)
    {
        return ToJsonPtr(new NativeBoolResult(res), NativeJsonContext.Default.NativeBoolResult);
    }

    #endregion

    #region 0. 初始化与内存管理

    [UnmanagedCallersOnly(EntryPoint = "KgInitSdk")]
    public static IntPtr KgInitSdk()
    {
        try
        {
            var (transport, sessionMgr) = KgHttpClientFactory.CreateWithSession(new NativeSessionPersistence());
            _sessionManager = sessionMgr;

            // 组装所有的 RawApi 和 Client
            var rawLogin = new RawLoginApi(transport, _sessionManager, NullLogger<RawLoginApi>.Instance);
            var rawSearch = new RawSearchApi(transport);
            var rawDevice = new RawDeviceApi(transport, _sessionManager, NullLogger<RawDeviceApi>.Instance);
            var rawUser = new RawUserApi(transport);
            var rawPlaylist = new RawPlaylistApi(transport, NullLogger<RawPlaylistApi>.Instance);
            var rawLyric = new RawLyricApi(transport);
            var rawDiscovery = new RawDiscoveryApi(transport);
            var rawRank = new RawRankApi(transport);
            var rawAlbum = new RawAlbumApi(transport);
            var rawSong = new RawSongApi(transport, _sessionManager);
            var rawArtist = new RawArtistApi(transport, _sessionManager);

            _loginClient = new LoginClient(rawLogin, _sessionManager, NullLogger<LoginClient>.Instance);
            _registerClient = new RegisterClient(rawDevice, _sessionManager, NullLogger<RegisterClient>.Instance);
            _searchClient = new SearchClient(rawSearch, _sessionManager);
            _songClient = new SongClient(rawSong, rawSearch);
            _artistClient = new ArtistClient(rawArtist, rawSearch, _sessionManager);
            _playlistClient = new PlaylistClient(rawPlaylist, _sessionManager);
            _userClient = new UserClient(rawUser, _sessionManager);
            _lyricClient = new LyricClient(rawLyric);
            _recommendClient = new RecommendClient(rawDiscovery, _sessionManager);
            _rankClient = new RankClient(rawRank);
            _albumClient = new AlbumClient(rawAlbum);

            return ReturnBool(true);
        }
        catch (Exception ex)
        {
            return ReturnError(ex.Message);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "KgFreeMemory")]
    public static void KgFreeMemory(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero) Marshal.FreeCoTaskMem(ptr);
    }

    #endregion

    #region 1. Auth & Device

    [UnmanagedCallersOnly(EntryPoint = "KgAuth_SendCode")]
    public static IntPtr KgAuth_SendCode(IntPtr mobilePtr)
    {
        return ToJsonPtr(_loginClient!.SendCodeAsync(GetStr(mobilePtr)).GetAwaiter().GetResult(),
            NativeJsonContext.Default.SendCodeResponse);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgAuth_LoginByMobile")]
    public static IntPtr KgAuth_LoginByMobile(IntPtr mobilePtr, IntPtr codePtr)
    {
        return ToJsonPtr(_loginClient!.LoginByMobileAsync(GetStr(mobilePtr), GetStr(codePtr)).GetAwaiter().GetResult(),
            NativeJsonContext.Default.LoginResponse);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgAuth_GetQrCode")]
    public static IntPtr KgAuth_GetQrCode()
    {
        return ToJsonPtr(_loginClient!.GetQrCodeAsync().GetAwaiter().GetResult(), NativeJsonContext.Default.QRCode);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgAuth_CheckQrStatus")]
    public static IntPtr KgAuth_CheckQrStatus(IntPtr keyPtr)
    {
        return ToJsonPtr(_loginClient!.CheckQrStatusAsync(GetStr(keyPtr)).GetAwaiter().GetResult(),
            NativeJsonContext.Default.QrLoginStatusResponse);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgAuth_RefreshSession")]
    public static IntPtr KgAuth_RefreshSession()
    {
        return ToJsonPtr(_loginClient!.RefreshSessionAsync().GetAwaiter().GetResult(),
            NativeJsonContext.Default.RefreshTokenResponse);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgAuth_LogOut")]
    public static IntPtr KgAuth_LogOut()
    {
        _loginClient!.LogOutAsync();
        return ReturnBool(true);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgDevice_InitDevice")]
    public static IntPtr KgDevice_InitDevice()
    {
        return ReturnBool(_registerClient!.InitDeviceAsync().GetAwaiter().GetResult());
    }

    #endregion

    #region 2. Discovery & Lyric

    [UnmanagedCallersOnly(EntryPoint = "KgDiscovery_GetRecommendedSongs")]
    public static IntPtr KgDiscovery_GetRecommendedSongs()
    {
        return ToJsonPtr(_recommendClient!.GetRecommendedSongsAsync().GetAwaiter().GetResult(),
            NativeJsonContext.Default.DailyRecommendResponse);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgLyric_GetLyric")]
    public static IntPtr KgLyric_GetLyric(IntPtr idPtr, IntPtr accessKeyPtr, IntPtr fmtPtr, int decode)
    {
        return ToJsonPtr(
            _lyricClient!.GetLyricAsync(GetStr(idPtr), GetStr(accessKeyPtr), GetStr(fmtPtr), decode == 1).GetAwaiter()
                .GetResult(), NativeJsonContext.Default.LyricResult);
    }

    #endregion

    #region 3. Music (Search)

    [UnmanagedCallersOnly(EntryPoint = "KgMusic_Search")]
    public static IntPtr KgMusic_Search(IntPtr keywordPtr, int page, IntPtr typePtr)
    {
        return ToJsonPtr(_searchClient!.SearchAsync(GetStr(keywordPtr), page, GetStr(typePtr)).GetAwaiter().GetResult(),
            NativeJsonContext.Default.ListSongInfo);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgMusic_GetPlayInfo")]
    public static IntPtr KgMusic_GetPlayInfo(IntPtr hashPtr, IntPtr qualityPtr)
    {
        return ToJsonPtr(_songClient!.GetPlayInfoAsync(GetStr(hashPtr), GetStr(qualityPtr)).GetAwaiter().GetResult(),
            NativeJsonContext.Default.PlayUrlData);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgMusic_GetSearchHot")]
    public static IntPtr KgMusic_GetSearchHot()
    {
        return ToJsonPtr(_searchClient!.GetSearchHotAsync().GetAwaiter().GetResult(),
            NativeJsonContext.Default.SearchHotResponse);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgMusic_GetSingerSongs")]
    public static IntPtr KgMusic_GetSingerSongs(IntPtr authorIdPtr, int page, int pageSize, IntPtr sortPtr)
    {
        return ToJsonPtr(
            _artistClient!.GetAudiosAsync(GetStr(authorIdPtr), page, pageSize, GetStr(sortPtr)).GetAwaiter()
                .GetResult(), NativeJsonContext.Default.SingerAudioResponse);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgMusic_GetSingerDetail")]
    public static IntPtr KgMusic_GetSingerDetail(IntPtr authorIdPtr)
    {
        return ToJsonPtr(_artistClient!.GetDetailAsync(GetStr(authorIdPtr)).GetAwaiter().GetResult(),
            NativeJsonContext.Default.SingerDetailResponse);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgMusic_SearchPlaylists")]
    public static IntPtr KgMusic_SearchPlaylists(IntPtr keywordPtr, int page)
    {
        return ToJsonPtr(_searchClient!.SearchSpecialAsync(GetStr(keywordPtr), page).GetAwaiter().GetResult(),
            NativeJsonContext.Default.ListSearchPlaylistItem);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgMusic_SearchAlbums")]
    public static IntPtr KgMusic_SearchAlbums(IntPtr keywordPtr, int page)
    {
        return ToJsonPtr(_searchClient!.SearchAlbumAsync(GetStr(keywordPtr), page).GetAwaiter().GetResult(),
            NativeJsonContext.Default.ListSearchAlbumItem);
    }

    #endregion

    #region 4. Playlist & Album & Rank

    [UnmanagedCallersOnly(EntryPoint = "KgPlaylist_GetSongs")]
    public static IntPtr KgPlaylist_GetSongs(IntPtr playlistIdPtr, int page, int pageSize)
    {
        return ToJsonPtr(_playlistClient!.GetSongsAsync(GetStr(playlistIdPtr), page, pageSize).GetAwaiter().GetResult(),
            NativeJsonContext.Default.PlaylistSongResponse);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgPlaylist_GetInfo")]
    public static IntPtr KgPlaylist_GetInfo(IntPtr playlistIdPtr)
    {
        return ToJsonPtr(_playlistClient!.GetInfoAsync(GetStr(playlistIdPtr)).GetAwaiter().GetResult(),
            NativeJsonContext.Default.PlaylistInfo);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgPlaylist_AddSongs")]
    public static IntPtr KgPlaylist_AddSongs(IntPtr playlistIdPtr, IntPtr jsonSongsPtr)
    {
        try
        {
            var dtoList = JsonSerializer.Deserialize(GetStr(jsonSongsPtr),
                NativeJsonContext.Default.NativeAddSongItemDtoArray);
            var tupleList = dtoList?.Select(x => (x.Name, x.Hash, x.AlbumId, x.MixSongId)).ToList();
            var res = _playlistClient!.AddSongsAsync(GetStr(playlistIdPtr),
                    tupleList ?? new List<(string Name, string Hash, string AlbumId, string MixSongId)>()).GetAwaiter()
                .GetResult();
            return ToJsonPtr(res, NativeJsonContext.Default.AddSongResponse);
        }
        catch (Exception ex)
        {
            return ReturnError(ex.Message);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "KgPlaylist_RemoveSongs")]
    public static IntPtr KgPlaylist_RemoveSongs(IntPtr playlistIdPtr, IntPtr jsonFileIdsPtr)
    {
        try
        {
            var ids = JsonSerializer.Deserialize(GetStr(jsonFileIdsPtr), NativeJsonContext.Default.Int64Array);
            var res = _playlistClient!.RemoveSongsAsync(GetStr(playlistIdPtr), ids ?? Array.Empty<long>()).GetAwaiter()
                .GetResult();
            return ToJsonPtr(res, NativeJsonContext.Default.RemoveSongResponse);
        }
        catch (Exception ex)
        {
            return ReturnError(ex.Message);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "KgAlbum_GetSongs")]
    public static IntPtr KgAlbum_GetSongs(IntPtr albumIdPtr, int page, int pageSize)
    {
        return ToJsonPtr(_albumClient!.GetSongsAsync(GetStr(albumIdPtr), page, pageSize).GetAwaiter().GetResult(),
            NativeJsonContext.Default.ListAlbumSongItem);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgRank_GetAllRanks")]
    public static IntPtr KgRank_GetAllRanks(int withSong)
    {
        return ToJsonPtr(_rankClient!.GetAllRanksAsync(withSong).GetAwaiter().GetResult(),
            NativeJsonContext.Default.RankListResponse);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgRank_GetRankSongs")]
    public static IntPtr KgRank_GetRankSongs(int rankId, int page, int pageSize)
    {
        return ToJsonPtr(_rankClient!.GetRankSongsAsync(rankId, page, pageSize).GetAwaiter().GetResult(),
            NativeJsonContext.Default.RankSongResponse);
    }

    #endregion

    #region 5. User & VIP

    [UnmanagedCallersOnly(EntryPoint = "KgUser_GetUserInfo")]
    public static IntPtr KgUser_GetUserInfo()
    {
        return ToJsonPtr(_userClient!.GetUserInfoAsync().GetAwaiter().GetResult(),
            NativeJsonContext.Default.UserDetailModel);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgUser_GetVipInfo")]
    public static IntPtr KgUser_GetVipInfo()
    {
        return ToJsonPtr(_userClient!.GetVipInfoAsync().GetAwaiter().GetResult(),
            NativeJsonContext.Default.UserVipResponse);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgUser_GetVipRecord")]
    public static IntPtr KgUser_GetVipRecord()
    {
        return ToJsonPtr(_userClient!.GetVipRecordAsync().GetAwaiter().GetResult(),
            NativeJsonContext.Default.VipReceiveHistoryResponse);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgUser_GetPlaylists")]
    public static IntPtr KgUser_GetPlaylists(int page, int pageSize)
    {
        return ToJsonPtr(_userClient!.GetPlaylistsAsync(page, pageSize).GetAwaiter().GetResult(),
            NativeJsonContext.Default.UserPlaylistResponse);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgUser_ReceiveOneDayVip")]
    public static IntPtr KgUser_ReceiveOneDayVip()
    {
        return ToJsonPtr(_userClient!.ReceiveOneDayVipAsync().GetAwaiter().GetResult(),
            NativeJsonContext.Default.OneDayVipModel);
    }

    [UnmanagedCallersOnly(EntryPoint = "KgUser_UpgradeVipReward")]
    public static IntPtr KgUser_UpgradeVipReward()
    {
        return ToJsonPtr(_userClient!.UpgradeVipRewardAsync().GetAwaiter().GetResult(),
            NativeJsonContext.Default.UpgradeVipModel);
    }

    #endregion
}
