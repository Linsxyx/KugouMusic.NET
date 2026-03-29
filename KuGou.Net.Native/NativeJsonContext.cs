using System.Text.Json.Serialization;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Adapters.Lyrics;

namespace KuGou.Net.Native;

public record NativeErrorResult(string Error);

public record NativeBoolResult(bool Result);

public record NativeAddSongItemDto(string Name, string Hash, string AlbumId, string MixSongId);

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
// 基础响应与异常
[JsonSerializable(typeof(NativeErrorResult))]
[JsonSerializable(typeof(NativeBoolResult))]
// Auth
[JsonSerializable(typeof(LoginResponse))]
[JsonSerializable(typeof(SendCodeResponse))]
[JsonSerializable(typeof(QRCode))]
[JsonSerializable(typeof(QrLoginStatusResponse))]
[JsonSerializable(typeof(RefreshTokenResponse))]
// Discovery
[JsonSerializable(typeof(DailyRecommendResponse))]
// Lyric
[JsonSerializable(typeof(LyricResult))]
// Music
[JsonSerializable(typeof(List<SongInfo>))]
[JsonSerializable(typeof(PlayUrlData))]
[JsonSerializable(typeof(SearchHotResponse))]
[JsonSerializable(typeof(SingerAudioResponse))]
[JsonSerializable(typeof(SingerDetailResponse))]
[JsonSerializable(typeof(List<SearchPlaylistItem>))]
[JsonSerializable(typeof(List<SearchAlbumItem>))]
// Playlist
[JsonSerializable(typeof(List<PlaylistSong>))]
[JsonSerializable(typeof(PlaylistInfo))]
[JsonSerializable(typeof(AddSongResponse))]
[JsonSerializable(typeof(RemoveSongResponse))]
[JsonSerializable(typeof(NativeAddSongItemDto[]))]
[JsonSerializable(typeof(long[]))]
// Rank
[JsonSerializable(typeof(RankListResponse))]
[JsonSerializable(typeof(RankSongResponse))]
// User & Vip
[JsonSerializable(typeof(UserDetailModel))]
[JsonSerializable(typeof(UserVipResponse))]
[JsonSerializable(typeof(VipReceiveHistoryResponse))]
[JsonSerializable(typeof(UserPlaylistResponse))]
[JsonSerializable(typeof(OneDayVipModel))]
[JsonSerializable(typeof(UpgradeVipModel))]
// Album
[JsonSerializable(typeof(List<AlbumSongItem>))]
internal partial class NativeJsonContext : JsonSerializerContext
{
}