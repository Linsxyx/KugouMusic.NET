using KaMusic.Cli.Models;

namespace KaMusic.Cli.Models;

// 终端私人 FM 的频道模式、选曲池、会话状态与播放请求模型（对齐桌面端 PersonalFmModels）。

internal enum TerminalPersonalFmMode
{
    Normal,
    Small,
    Peak
}

internal enum TerminalPersonalFmSongPoolId
{
    Taste = 0,
    Style = 1
}

internal sealed record TerminalPersonalFmPlaybackRequest(
    TerminalSongItem Song,
    IReadOnlyList<TerminalSongItem> ContextList);

internal sealed record TerminalPersonalFmAdvanceResult(
    bool Success,
    TerminalPersonalFmPlaybackRequest? PlaybackRequest = null,
    string? UnavailableMessage = null);

internal static class TerminalPersonalFmPresentation
{
    public static string GetModeApiValue(TerminalPersonalFmMode mode) => mode switch
    {
        TerminalPersonalFmMode.Small => "small",
        TerminalPersonalFmMode.Peak => "peak",
        _ => "normal"
    };

    public static TerminalPersonalFmMode ParseMode(string? mode) => mode?.ToLowerInvariant() switch
    {
        "small" => TerminalPersonalFmMode.Small,
        "peak" => TerminalPersonalFmMode.Peak,
        _ => TerminalPersonalFmMode.Normal
    };

    public static TerminalPersonalFmMode GetNextMode(TerminalPersonalFmMode mode) => mode switch
    {
        TerminalPersonalFmMode.Normal => TerminalPersonalFmMode.Small,
        TerminalPersonalFmMode.Small => TerminalPersonalFmMode.Peak,
        _ => TerminalPersonalFmMode.Normal
    };

    public static string GetTitle(TerminalPersonalFmMode mode) => mode switch
    {
        TerminalPersonalFmMode.Small => "小众 Radio",
        TerminalPersonalFmMode.Peak => "速览 Radio",
        _ => "红心 Radio"
    };

    public static string GetModeLabel(TerminalPersonalFmMode mode) => mode switch
    {
        TerminalPersonalFmMode.Small => "小众",
        TerminalPersonalFmMode.Peak => "速览",
        _ => "红心"
    };

    public static string GetSubtitle(TerminalPersonalFmMode mode) => mode switch
    {
        TerminalPersonalFmMode.Small => "给你一点不那么主流的惊喜",
        TerminalPersonalFmMode.Peak => "切到更带劲的高能推荐频道",
        _ => "根据你的口味持续刷新"
    };

    public static string GetSongPoolLabel(TerminalPersonalFmSongPoolId songPoolId) =>
        songPoolId == TerminalPersonalFmSongPoolId.Style ? "根据风格" : "根据口味";

    public static string GetSongPoolShortLabel(TerminalPersonalFmSongPoolId songPoolId) =>
        songPoolId == TerminalPersonalFmSongPoolId.Style ? "风格" : "口味";
}

internal sealed class TerminalPersonalFmActionContext
{
    public TerminalSongItem? Track { get; init; }
    public string Action { get; init; } = "play";
    public int RemainSongCount { get; init; }
    public int? PlaytimeSeconds { get; init; }
    public bool? IsOverplay { get; init; }
}

internal sealed class TerminalPersonalFmSessionState
{
    public TerminalPersonalFmMode Mode { get; set; } = TerminalPersonalFmMode.Normal;
    public TerminalPersonalFmSongPoolId SongPoolId { get; set; } = TerminalPersonalFmSongPoolId.Taste;
    public bool IsActive { get; set; }
    public TerminalSongItem? CurrentSong { get; set; }
    public List<TerminalSongItem> UpcomingSongs { get; } = [];
    public List<TerminalSongItem> HistorySongs { get; } = [];

    public void Reset(
        TerminalPersonalFmMode mode,
        TerminalPersonalFmSongPoolId songPoolId,
        IEnumerable<TerminalSongItem> songs,
        TerminalSongItem? currentSong)
    {
        Mode = mode;
        SongPoolId = songPoolId;
        IsActive = currentSong != null;
        CurrentSong = currentSong;
        UpcomingSongs.Clear();
        HistorySongs.Clear();

        foreach (var song in songs)
        {
            if (currentSong != null && ReferenceEquals(song, currentSong))
            {
                continue;
            }

            UpcomingSongs.Add(song);
        }
    }

    public void Clear()
    {
        IsActive = false;
        CurrentSong = null;
        UpcomingSongs.Clear();
        HistorySongs.Clear();
    }

    public IReadOnlyList<TerminalSongItem> GetDisplaySongs(int limit = 5)
    {
        var result = new List<TerminalSongItem>(Math.Max(limit, 1));
        if (CurrentSong != null)
        {
            result.Add(CurrentSong);
        }

        foreach (var song in UpcomingSongs)
        {
            if (result.Count >= limit)
            {
                break;
            }

            result.Add(song);
        }

        return result;
    }
}
