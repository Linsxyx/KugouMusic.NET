using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KugouAvaloniaPlayer.Converters;
using KugouAvaloniaPlayer.Services;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class LocalMusicSearchDialogViewModel : ObservableObject, IDisposable
{
    private readonly ILocalMusicLibraryService _localMusicLibraryService;
    private readonly Func<LocalTrackSearchResult, Task> _openResultAction;
    private readonly Action _cancelAction;
    private CancellationTokenSource? _searchCancellation;
    private long _searchVersion;

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPromptState))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial bool HasSearched { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPromptState))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial bool IsSearching { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial string? ErrorMessage { get; set; }

    public LocalMusicSearchDialogViewModel(
        ILocalMusicLibraryService localMusicLibraryService,
        Func<LocalTrackSearchResult, Task> openResultAction,
        Action cancelAction)
    {
        _localMusicLibraryService = localMusicLibraryService;
        _openResultAction = openResultAction;
        _cancelAction = cancelAction;
    }

    public ObservableCollection<GroupedSearchResultItemViewModel> Results { get; } = new();

    public bool HasResults => Results.Count > 0;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool ShowPromptState => !HasSearched && !IsSearching;
    public bool ShowEmptyState => HasSearched && !IsSearching && !HasResults && !HasError;

    [RelayCommand]
    private async Task SearchAsync()
    {
        var keyword = SearchText?.Trim();
        var searchVersion = Interlocked.Increment(ref _searchVersion);

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;

        Results.Clear();
        NotifyResultStateChanged();
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(keyword))
        {
            HasSearched = false;
            IsSearching = false;
            return;
        }

        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        HasSearched = true;
        IsSearching = true;

        try
        {
            var results = await _localMusicLibraryService.SearchDistinctTracksAsync(keyword, cancellation.Token);
            if (searchVersion != _searchVersion || cancellation.IsCancellationRequested)
                return;

            foreach (var result in results)
            {
                Results.Add(new GroupedSearchResultItemViewModel(
                    _localMusicLibraryService,
                    result,
                    _openResultAction));
            }

            NotifyResultStateChanged();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (searchVersion == _searchVersion)
                ErrorMessage = ex.Message;
        }
        finally
        {
            if (searchVersion == _searchVersion)
            {
                IsSearching = false;
                NotifyResultStateChanged();
            }
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CancelPendingSearch();
        _cancelAction();
    }

    public void Dispose()
    {
        CancelPendingSearch();
        GC.SuppressFinalize(this);
    }

    private void CancelPendingSearch()
    {
        Interlocked.Increment(ref _searchVersion);
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;
    }

    private void NotifyResultStateChanged()
    {
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(ShowEmptyState));
    }
}

public partial class GroupedSearchResultItemViewModel : ObservableObject
{
    private readonly ILocalMusicLibraryService _libraryService;
    private readonly Func<LocalTrackSearchResult, Task> _openResultAction;
    private bool _playlistsLoaded;

    public GroupedSearchResultItemViewModel(
        ILocalMusicLibraryService libraryService,
        LocalTrackSearchResult result,
        Func<LocalTrackSearchResult, Task> openResultAction)
    {
        _libraryService = libraryService;
        _openResultAction = openResultAction;
        Result = result;
        Cover = ResolveCover(result.Track);
    }

    public LocalTrackSearchResult Result { get; }
    public string Title => Result.Track.Title;
    public string Artist => Result.Track.Artist;
    public string Album => string.IsNullOrWhiteSpace(Result.Track.Album) ? "未知专辑" : Result.Track.Album;
    public double DurationSeconds => Result.Track.DurationSeconds;
    public string Cover { get; }
    public long TrackId => Result.Track.Id;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingPlaylists { get; set; }

    public ObservableCollection<PlaylistSummaryItem> Playlists { get; } = new();

    [RelayCommand]
    private async Task ToggleExpandAsync() {
        IsExpanded = !IsExpanded;
        if (!IsExpanded)
            return;

        if (_playlistsLoaded)
            return;

        IsLoadingPlaylists = true;
        try
        {
            var playlists = await _libraryService.GetTrackPlaylistsAsync(TrackId);
            Playlists.Clear();
            foreach (var p in playlists)
            {
                Playlists.Add(new PlaylistSummaryItem
                {
                    PlaylistId = p.Id,
                    PlaylistName = p.Name,
                    PlaylistCover = ResolvePlaylistCover(p.CoverPath),
                    TrackCount = p.TrackCount,
                    Track = Result.Track
                });
            }

            _playlistsLoaded = true;
        }
        finally
        {
            IsLoadingPlaylists = false;
        }
    }

    [RelayCommand]
    private async Task OpenPlaylistAsync(PlaylistSummaryItem? item)
    {
        if (item is null)
            return;

        var searchResult = new LocalTrackSearchResult(
            item.Track,
            item.PlaylistId,
            item.PlaylistName,
            0);

        await _openResultAction(searchResult);
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

    private static string ResolveCover(LocalTrackItem track)
    {
        const string defaultSongCover = "avares://KugouAvaloniaPlayer/Assets/default_song.png";

        if (string.Equals(track.SourceType, LocalMusicLibraryService.SourceTypeJellyfin, StringComparison.Ordinal))
            return string.IsNullOrWhiteSpace(track.CoverPath) ? defaultSongCover : track.CoverPath;

        if (!string.IsNullOrWhiteSpace(track.CoverPath))
        {
            if (LocalImageSourceHelper.TryGetEmbeddedCoverFilePath(track.CoverPath, out _))
                return track.CoverPath;

            if (File.Exists(track.CoverPath))
                return new Uri(track.CoverPath).AbsoluteUri;
        }

        return string.IsNullOrWhiteSpace(track.LocalPath) || !File.Exists(track.LocalPath)
            ? defaultSongCover
            : LocalImageSourceHelper.BuildEmbeddedCoverSource(track.LocalPath);
    }
}

public sealed class PlaylistSummaryItem
{
    public long PlaylistId { get; init; }
    public string PlaylistName { get; init; } = "";
    public string PlaylistCover { get; init; } = "";
    public int TrackCount { get; init; }
    public LocalTrackItem Track { get; init; } = null!;
}
