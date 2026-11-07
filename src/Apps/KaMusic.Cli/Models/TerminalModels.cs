using KuGou.Net.Abstractions;
using KaMusic.Cli.Graphics;

namespace KaMusic.Cli.Models;

// 终端播放器的页面、歌曲/歌单/歌词视图模型与持久化配置。

internal enum TerminalPage
{
    Daily,
    PersonalFm,
    Discover,
    Ranks,
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
    Discover
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
    public string Name { get; set; } = "";
    public string Singer { get; set; } = "";
    public string Hash { get; set; } = "";
    public string AlbumId { get; set; } = "";
    public string MixSongId { get; set; } = "";
    public long AudioId { get; set; }
    public string? Cover { get; set; }
    public double DurationSeconds { get; set; }
    public string Source { get; set; } = "";
    public int Privilege { get; set; }
    public bool IsPlaying { get; set; }
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
