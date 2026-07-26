using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KugouAvaloniaPlayer.Converters;
using KugouAvaloniaPlayer.Services;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class TrackPlaylistSearchDialogViewModel : ObservableObject, IDisposable
{
    private readonly ILocalMusicLibraryService _localMusicLibraryService;
    private readonly Func<LocalTrackSearchResult, Task> _openResultAction;
    private readonly Action _cancelAction;
    private bool _playlistsLoaded;

    public TrackPlaylistSearchDialogViewModel(
        ILocalMusicLibraryService localMusicLibraryService,
        SongItem song,
        Func<LocalTrackSearchResult, Task> openResultAction,
        Action cancelAction)
    {
        _localMusicLibraryService = localMusicLibraryService;
        _openResultAction = openResultAction;
        _cancelAction = cancelAction;

        Title = song.Name;
        Artist = song.Singer;
        Album = string.IsNullOrWhiteSpace(song.AlbumName) ? "未知专辑" : song.AlbumName;
        Duration = song.DurationSeconds;
        Cover = song.Cover ?? "avares://KugouAvaloniaPlayer/Assets/default_song.png";
        TrackId = song.LocalTrackId;

        _ = LoadPlaylistsAsync();
    }

    public string Title { get; }
    public string Artist { get; }
    public string Album { get; }
    public double Duration { get; }
    public string Cover { get; }
    public long TrackId { get; }

    public ObservableCollection<TrackPlaylistItem> Playlists { get; } = new();

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    private async Task LoadPlaylistsAsync()
    {
        if (_playlistsLoaded)
            return;

        IsLoading = true;
        try
        {
            var playlists = await _localMusicLibraryService.GetTrackPlaylistsAsync(TrackId);
            Playlists.Clear();
            foreach (var p in playlists)
            {
                Playlists.Add(new TrackPlaylistItem
                {
                    PlaylistId = p.Id,
                    PlaylistName = p.Name,
                    PlaylistCover = ResolvePlaylistCover(p.CoverPath),
                    TrackCount = p.TrackCount
                });
            }

            _playlistsLoaded = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task OpenPlaylistAsync(TrackPlaylistItem? item)
    {
        if (item is null)
            return;

        var searchResult = new LocalTrackSearchResult(
            new LocalTrackItem(
                TrackId,
                Title,
                Artist,
                Album,
                Duration,
                string.Empty,
                string.Empty,
                null,
                null),
            item.PlaylistId,
            item.PlaylistName,
            0);

        _cancelAction();
        await _openResultAction(searchResult);
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancelAction();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private static string ResolvePlaylistCover(string? coverPath)
    {
        const string defaultPlaylistCover = "avares://KugouAvaloniaPlayer/Assets/default_listcard.png";

        if (string.IsNullOrWhiteSpace(coverPath))
            return defaultPlaylistCover;

        if (LocalImageSourceHelper.TryGetEmbeddedCoverFilePath(coverPath, out _))
            return coverPath;

        if (Uri.TryCreate(coverPath, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == "avares"))
        {
            return coverPath;
        }

        return File.Exists(coverPath) ? new Uri(coverPath).AbsoluteUri : defaultPlaylistCover;
    }
}

public sealed class TrackPlaylistItem
{
    public long PlaylistId { get; init; }
    public string PlaylistName { get; init; } = "";
    public string PlaylistCover { get; init; } = "";
    public int TrackCount { get; init; }
}
