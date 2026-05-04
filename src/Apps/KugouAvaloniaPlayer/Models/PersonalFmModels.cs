using System;
using System.Collections.Generic;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Models;

public enum SongPlaybackSource
{
    Default,
    PersonalFm
}

public enum PersonalFmMode
{
    Normal,
    Small,
    Peak
}

public enum PersonalFmSongPoolId
{
    Taste = 0,
    Style = 1
}

public sealed record PersonalFmSongPoolOption(PersonalFmSongPoolId Value, string Label);

public static class PersonalFmPresentation
{
    public static string GetModeApiValue(PersonalFmMode mode)
    {
        return mode switch
        {
            PersonalFmMode.Small => "small",
            PersonalFmMode.Peak => "peak",
            _ => "normal"
        };
    }

    public static PersonalFmMode ParseMode(string? mode)
    {
        return mode?.ToLowerInvariant() switch
        {
            "small" => PersonalFmMode.Small,
            "peak" => PersonalFmMode.Peak,
            _ => PersonalFmMode.Normal
        };
    }

    public static string GetTitle(PersonalFmMode mode)
    {
        return mode switch
        {
            PersonalFmMode.Small => "小众 Radio",
            PersonalFmMode.Peak => "巅峰 Radio",
            _ => "红心 Radio"
        };
    }

    public static string GetModeLabel(PersonalFmMode mode)
    {
        return mode switch
        {
            PersonalFmMode.Small => "小众",
            PersonalFmMode.Peak => "巅峰",
            _ => "红心"
        };
    }

    public static string GetSubtitle(PersonalFmMode mode)
    {
        return mode switch
        {
            PersonalFmMode.Small => "给你一点不那么主流的惊喜",
            PersonalFmMode.Peak => "切到更带劲的高能推荐频道",
            _ => "根据你的口味持续刷新"
        };
    }

    public static string GetSongPoolLabel(PersonalFmSongPoolId songPoolId)
    {
        return songPoolId == PersonalFmSongPoolId.Style ? "根据风格" : "根据口味";
    }
}

internal sealed class PersonalFmActionContext
{
    public SongItem? Track { get; init; }
    public string Action { get; init; } = "play";
    public int RemainSongCount { get; init; }
    public int? PlaytimeSeconds { get; init; }
    public bool? IsOverplay { get; init; }
}

internal sealed class PersonalFmSessionState
{
    public PersonalFmMode Mode { get; set; } = PersonalFmMode.Normal;
    public PersonalFmSongPoolId SongPoolId { get; set; } = PersonalFmSongPoolId.Taste;
    public bool IsActive { get; set; }
    public SongItem? CurrentSong { get; set; }
    public List<SongItem> UpcomingSongs { get; } = [];
    public List<SongItem> HistorySongs { get; } = [];

    public void Reset(PersonalFmMode mode, PersonalFmSongPoolId songPoolId, IEnumerable<SongItem> songs, SongItem? currentSong)
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
                continue;

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

    public IReadOnlyList<SongItem> GetDisplaySongs(int limit = 5)
    {
        var result = new List<SongItem>(Math.Max(limit, 1));
        if (CurrentSong != null)
            result.Add(CurrentSong);

        foreach (var song in UpcomingSongs)
        {
            if (result.Count >= limit)
                break;

            result.Add(song);
        }

        return result;
    }
}
