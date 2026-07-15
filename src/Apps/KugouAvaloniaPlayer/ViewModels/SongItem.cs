using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KuGou.Net.Abstractions.Models;
using KugouAvaloniaPlayer.Models;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class SongItem : ObservableObject
{
    [ObservableProperty]
    public partial string AlbumId { get; set; } = "";

    [ObservableProperty]
    public partial long AlbumAudioId { get; set; }

    [ObservableProperty]
    public partial string AlbumName { get; set; } = "";

    [ObservableProperty]
    public partial long AudioId { get; set; }

    [ObservableProperty]
    public partial string? Cover { get; set; } = "avares://KugouAvaloniaPlayer/Assets/default_song.png";

    [ObservableProperty]
    public partial double DurationSeconds { get; set; }

    [ObservableProperty]
    public partial long FileId { get; set; }

    public string Hash { get; init; } = "";

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    public string? LocalFilePath { get; init; }

    [ObservableProperty]
    public partial string? LocalSourceType { get; set; }

    [ObservableProperty]
    public partial long LocalTrackId { get; set; }

    public string? RemoteUrl { get; init; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial SongPlaybackSource PlaybackSource { get; set; } = SongPlaybackSource.Default;

    [ObservableProperty]
    public partial string Singer { get; set; } = "";

    public List<SingerLite> Singers { get; set; } = new();

    public string DisplayTitle => NormalizeDisplayTitle(Name, Singer);

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayTitle));
    }

    partial void OnSingerChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayTitle));
    }

    [RelayCommand]
    private void Play()
    {
        WeakReferenceMessenger.Default.Send(new PlaySongMessage(this));
    }

    [RelayCommand]
    private void AddToNext()
    {
        WeakReferenceMessenger.Default.Send(new AddToNextMessage(this));
    }

    [RelayCommand]
    private void ShowPlaylistDialog()
    {
        WeakReferenceMessenger.Default.Send(new ShowPlaylistDialogMessage(this));
    }

    [RelayCommand]
    private void ViewSinger(SingerLite? singer)
    {
        if (singer != null)
            WeakReferenceMessenger.Default.Send(new NavigateToSingerMessage(singer));
    }

    [RelayCommand]
    private void RemoveFromPlaylist()
    {
        WeakReferenceMessenger.Default.Send(new RemoveFromPlaylistMessage(this));
    }

    [RelayCommand]
    private void SetLocalCover()
    {
        WeakReferenceMessenger.Default.Send(new SetLocalSongCoverMessage(this));
    }

    private static string NormalizeDisplayTitle(string name, string singer)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(singer))
            return name;

        var trimmedName = name.Trim();
        var trimmedSinger = singer.Trim();
        var separators = new[] { " - ", "-", "–", "—", ":", "：" };

        foreach (var separator in separators)
        {
            var prefix = trimmedSinger + separator;
            if (trimmedName.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
                return trimmedName[prefix.Length..].Trim();
        }

        return trimmedName;
    }

    public static bool operator ==(SongItem? a, SongItem? b) =>
        (a is null && b is null) ||
        (!string.IsNullOrWhiteSpace(a?.LocalFilePath) && a.LocalFilePath == b?.LocalFilePath) ||
        (!string.IsNullOrWhiteSpace(a?.RemoteUrl) && a.RemoteUrl == b?.RemoteUrl && a.Hash == b.Hash);

    public static bool operator !=(SongItem? a, SongItem? b) => !(a == b);

    public override bool Equals(object? obj) => obj is SongItem item && this == item;

    public override int GetHashCode() =>
        string.IsNullOrWhiteSpace(Hash) ?
            HashCode.Combine(LocalFilePath ?? RemoteUrl) :
            HashCode.Combine(LocalFilePath ?? RemoteUrl, Hash);
}

public partial class PlaylistItem : ObservableObject
{
    [ObservableProperty]
    public partial int Count { get; set; }

    [ObservableProperty]
    public partial string Cover { get; set; } = "avares://KugouAvaloniaPlayer/Assets/default_song.png";

    [ObservableProperty]
    public partial string Id { get; set; } = "";

    [ObservableProperty]
    public partial long ListId { get; set; }

    [ObservableProperty]
    public partial string? LocalPath { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Subtitle { get; set; } = "";

    [ObservableProperty]
    public partial PlaylistType Type { get; set; }

    [ObservableProperty]
    public partial int UserPlaylistType { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
