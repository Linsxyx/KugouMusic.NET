using KuGou.Net.Abstractions;
using KuGou.Net.Abstractions.Models;
using KaMusic.Cli.Graphics;

namespace KaMusic.Cli.Models;

// 终端播放器的页面、歌曲/歌单/歌词视图模型与持久化配置。

internal enum TerminalPage
{
    Daily,
    PersonalFm,
    Discover,
    LocalMusic,
    Search,
    MyPlaylists,
    Queue,
    Settings,
    Account
}

internal enum TerminalPlaylistKind
{
    Online,
    Album,
    Rank,
    Discover,
    TopCard,
    Local
}

internal enum TerminalExploreTab
{
    Overview,
    TopCards,
    Playlists,
    Ranks
}

internal enum TerminalExploreCardKind
{
    TopCard,
    Playlist,
    Rank,
    Action
}

internal enum TerminalLyricMode
{
    Original,
    Translation,
    Romanization,
    Combined
}

internal sealed class TerminalSongItem
{
    // 歌单中的一次出现，复制到播放队列时保留；不能用 Hash 区分重复条目。
    internal object QueueIdentity { get; init; } = new();

    public string Name { get; set; } = "";
    public string Singer { get; set; } = "";
    public string AlbumName { get; set; } = "";
    public string Hash { get; set; } = "";
    public string AlbumId { get; set; } = "";
    public string MixSongId { get; set; } = "";
    public long AudioId { get; set; }
    public string? Cover { get; set; }
    public double DurationSeconds { get; set; }
    public string Source { get; set; } = "";
    public int Privilege { get; set; }
    public string? LocalFilePath { get; set; }
    public string? RemoteUrl { get; set; }
    public string? LocalSourceType { get; set; }
    public long LocalTrackId { get; set; }
    public bool IsPlaying { get; set; }
    public long AuthorId { get; set; }
    public List<SingerLite> Singers { get; set; } = [];
    public long FileId { get; set; }
    public bool IsFavorite { get; set; }

    public bool IsLocalFile => !string.IsNullOrWhiteSpace(LocalFilePath) && File.Exists(LocalFilePath);
    public bool IsRemoteStream => !string.IsNullOrWhiteSpace(RemoteUrl);
    public bool RequiresOnlineAuth => !IsLocalFile && !IsRemoteStream;

    public string PlaybackIdentity =>
        !string.IsNullOrWhiteSpace(LocalFilePath) ? $"local:{LocalFilePath}" :
        !string.IsNullOrWhiteSpace(RemoteUrl) ? $"remote:{RemoteUrl}" :
        $"hash:{Hash}";

    public bool IsSameTrack(TerminalSongItem? other)
    {
        if (other is null)
            return false;
        if (!string.IsNullOrWhiteSpace(LocalFilePath) || !string.IsNullOrWhiteSpace(other.LocalFilePath))
            return string.Equals(LocalFilePath, other.LocalFilePath, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(RemoteUrl) || !string.IsNullOrWhiteSpace(other.RemoteUrl))
            return string.Equals(RemoteUrl, other.RemoteUrl, StringComparison.OrdinalIgnoreCase);
        return !string.IsNullOrWhiteSpace(Hash) && string.Equals(Hash, other.Hash, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class TerminalPlaylistItem
{
    public string Id { get; set; } = "";
    public string ListId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string? Cover { get; set; }
    public int Count { get; set; }
    public TerminalPlaylistKind Kind { get; set; }
    public long RankId { get; set; }
    public long LocalPlaylistId { get; set; }
    public string? LocalKind { get; set; }
    public string? SourceRef { get; set; }
    public IReadOnlyList<string> Previews { get; set; } = [];
    public IReadOnlyList<TerminalSongItem> PreloadedSongs { get; set; } = [];
}

// 歌单某一页的歌曲与其服务端声明的总歌曲数（0 表示服务端未提供）。
internal sealed record TerminalPlaylistSongsPage(IReadOnlyList<TerminalSongItem> Songs, int Total);

internal sealed class TerminalExploreCard
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Badge { get; init; } = "";
    public string SectionTitle { get; init; } = "";
    public string? Cover { get; init; }
    public TerminalExploreCardKind Kind { get; init; }
    public IReadOnlyList<string> PreviewLines { get; init; } = [];
    public TerminalPlaylistItem? Playlist { get; init; }
    public IReadOnlyList<TerminalSongItem> PreloadedSongs { get; init; } = [];
    public TerminalExploreTab? TargetTab { get; init; }
}

internal sealed class TerminalPlaylistTagItem
{
    public int Id { get; init; }
    public int CategoryId { get; init; }
    public string Name { get; init; } = "";
    public string GroupName { get; init; } = "";
}

internal sealed class TerminalLyricLineView
{
    public int Offset { get; init; }
    public bool IsCurrent { get; init; }
    public string Primary { get; init; } = "";
    public string? Secondary { get; init; }
    public IReadOnlyList<(string Text, float Progress)> Words { get; init; } = [];
}

internal sealed class TerminalUserProfile
{
    public bool IsLoggedIn { get; init; }
    public string UserId { get; init; } = "0";
    public string Name { get; init; } = "未登录";
    public string? AvatarUrl { get; init; }
    public string VipType { get; init; } = "0";
}

internal sealed class PlaybackStateSnapshot
{
    public TerminalSongItem? CurrentSong { get; init; }
    public IReadOnlyList<TerminalSongItem> Queue { get; init; } = [];
    public bool IsPlaying { get; init; }
    public bool IsPaused { get; init; }
    public bool IsShuffle { get; init; }
    public bool EnableSurround { get; init; }
    public bool EnableSeamlessTransition { get; init; }
    public bool IsCrossfading { get; init; }
    public float Volume { get; init; }
    public TimeSpan Position { get; init; }
    public TimeSpan Duration { get; init; }
    public TerminalLyricMode LyricMode { get; init; }
    public IReadOnlyList<string> LyricWindow { get; init; } = [];
    public IReadOnlyList<TerminalLyricLineView> LyricLines { get; init; } = [];
    public IReadOnlyList<float> SpectrumBands { get; init; } = [];
    public double Rms { get; init; }
}

internal sealed class TerminalAppSettings
{
    public string MusicQuality { get; set; } = AudioQuality.HiRes;
    public float Volume { get; set; } = 0.8f;
    public bool EnableSurround { get; set; }
    public bool EnableSeamlessTransition { get; set; }
    public bool Shuffle { get; set; }
    public TerminalLyricMode LyricMode { get; set; } = TerminalLyricMode.Translation;
    public TerminalImageMode ImageMode { get; set; } = TerminalImageMode.Auto;
    public float[] CustomEqGains { get; set; } = new float[10];
}

internal enum TerminalSingerTab
{
    HotSongs,
    NewSongs,
    Albums
}

internal sealed class TerminalSingerAlbumItem
{
    public long AlbumId { get; init; }
    public string Name { get; init; } = "";
    public string PublishDate { get; init; } = "";
    public string Cover { get; init; } = "";
    public int SongCount { get; init; }
    public string Subtitle { get; init; } = "";
}

internal sealed class TerminalSingerDetail
{
    public long AuthorId { get; init; }
    public string Name { get; init; } = "";
    public string? Avatar { get; init; }
    public int SongCount { get; init; }
    public int AlbumCount { get; init; }
    public string Intro { get; init; } = "";
    public TerminalSingerTab Tab { get; set; } = TerminalSingerTab.HotSongs;
    public List<TerminalSongItem> HotSongs { get; } = [];
    public List<TerminalSongItem> NewSongs { get; } = [];
    public List<TerminalSingerAlbumItem> Albums { get; } = [];
    public bool HasLoadedHotSongs { get; set; }
    public bool HasLoadedNewSongs { get; set; }
    public bool HasLoadedAlbums { get; set; }

    public IReadOnlyList<TerminalSongItem> CurrentSongs =>
        Tab == TerminalSingerTab.NewSongs ? NewSongs : HotSongs;
}
