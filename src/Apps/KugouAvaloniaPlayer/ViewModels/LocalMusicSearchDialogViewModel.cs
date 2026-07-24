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
    private readonly Func<LocalTrackSearchResult, Task> _openResultAction;
    private readonly Action _cancelAction;
    private readonly ILocalMusicLibraryService _localMusicLibraryService;
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

    public ObservableCollection<LocalMusicSearchResultItemViewModel> Results { get; } = new();

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
            var results = await _localMusicLibraryService.SearchTracksAsync(keyword, cancellation.Token);
            if (searchVersion != _searchVersion || cancellation.IsCancellationRequested)
                return;

            foreach (var result in results)
                Results.Add(new LocalMusicSearchResultItemViewModel(result));

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
    private async Task OpenResultAsync(LocalMusicSearchResultItemViewModel? item)
    {
        if (item is null || IsSearching)
            return;

        CancelPendingSearch();
        await _openResultAction(item.Result);
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

public sealed class LocalMusicSearchResultItemViewModel
{
    public LocalMusicSearchResultItemViewModel(LocalTrackSearchResult result)
    {
        Result = result;
        Cover = ResolveCover(result.Track);
    }

    public LocalTrackSearchResult Result { get; }
    public string Title => Result.Track.Title;
    public string Artist => Result.Track.Artist;
    public string Album => string.IsNullOrWhiteSpace(Result.Track.Album) ? "未知专辑" : Result.Track.Album;
    public string PlaylistName => Result.PlaylistName;
    public double DurationSeconds => Result.Track.DurationSeconds;
    public string Cover { get; }

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
