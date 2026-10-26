using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using ZLinq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class SongBatchActionDialogViewModel : ObservableObject
{
    private readonly List<SelectableSongItemViewModel> _allSongs;
    private readonly Func<IReadOnlyList<SongItem>, Task> _addToQueueAction;
    private readonly Func<IReadOnlyList<SongItem>, Task> _addToPlaylistAction;
    private readonly Action _cancelAction;
    private readonly Func<IReadOnlyList<SongItem>, Task> _playSelectedAction;

    [ObservableProperty]
    public partial string? FilterText { get; set; }

    [ObservableProperty]
    public partial bool IsSubmitting { get; set; }

    public SongBatchActionDialogViewModel(
        IEnumerable<SongItem> songs,
        bool allowAddToPlaylist,
        Func<IReadOnlyList<SongItem>, Task> addToQueueAction,
        Func<IReadOnlyList<SongItem>, Task> addToPlaylistAction,
        Func<IReadOnlyList<SongItem>, Task> playSelectedAction,
        Action cancelAction)
    {
        AllowAddToPlaylist = allowAddToPlaylist;
        _addToQueueAction = addToQueueAction;
        _addToPlaylistAction = addToPlaylistAction;
        _playSelectedAction = playSelectedAction;
        _cancelAction = cancelAction;
        _allSongs = songs.AsValueEnumerable()
            .Select(song => new SelectableSongItemViewModel(song))
            .ToList();

        foreach (var song in _allSongs)
        {
            song.PropertyChanged += OnSongPropertyChanged;
            FilteredSongs.Add(song);
        }

        FilteredSongs.CollectionChanged += OnFilteredSongsCollectionChanged;
        RefreshSelectionState();
    }

    public ObservableCollection<SelectableSongItemViewModel> FilteredSongs { get; } = new();

    public bool AllowAddToPlaylist { get; }
    public int TotalSongCount => _allSongs.Count;
    public bool HasSongs => _allSongs.Count > 0;
    public bool HasFilteredSongs => FilteredSongs.Count > 0;
    public int SelectedCount => _allSongs.AsValueEnumerable().Count(song => song.IsSelected);
    public bool CanExecuteBatchActions => SelectedCount > 0 && !IsSubmitting;

    partial void OnFilterTextChanged(string? value)
    {
        ApplyFilter(value);
    }

    partial void OnIsSubmittingChanged(bool value)
    {
        RefreshSelectionState();
    }

    [RelayCommand]
    private void ToggleSongSelection(SelectableSongItemViewModel? song)
    {
        if (song is null || IsSubmitting)
            return;

        song.IsSelected = !song.IsSelected;
    }

    [RelayCommand]
    private void SelectAllFiltered()
    {
        if (IsSubmitting)
            return;

        foreach (var song in FilteredSongs)
            song.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        if (IsSubmitting)
            return;

        foreach (var song in _allSongs)
            song.IsSelected = false;
    }

    [RelayCommand]
    private void Cancel()
    {
        if (IsSubmitting)
            return;

        _cancelAction();
    }

    [RelayCommand(CanExecute = nameof(CanExecuteBatchActions))]
    private async Task AddToQueueAsync()
    {
        await ExecuteBatchActionAsync(_addToQueueAction);
    }

    [RelayCommand(CanExecute = nameof(CanExecuteBatchActions))]
    private async Task AddToPlaylistAsync()
    {
        await ExecuteBatchActionAsync(_addToPlaylistAction);
    }

    [RelayCommand(CanExecute = nameof(CanExecuteBatchActions))]
    private async Task PlaySelectedAsync()
    {
        await ExecuteBatchActionAsync(_playSelectedAction);
    }

    private async Task ExecuteBatchActionAsync(Func<IReadOnlyList<SongItem>, Task> action)
    {
        var selectedSongs = GetSelectedSongs();
        if (selectedSongs.Count == 0)
            return;

        try
        {
            IsSubmitting = true;
            await action(selectedSongs);
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    private List<SongItem> GetSelectedSongs()
    {
        return _allSongs.AsValueEnumerable()
            .Where(song => song.IsSelected)
            .Select(song => song.Song)
            .ToList();
    }

    private void ApplyFilter(string? filter)
    {
        var keyword = filter?.Trim();
        IEnumerable<SelectableSongItemViewModel> source = _allSongs;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            source = source.AsValueEnumerable().Where(song =>
                song.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                song.Singer.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                song.AlbumName.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        FilteredSongs.Clear();
        foreach (var song in source)
            FilteredSongs.Add(song);

        OnPropertyChanged(nameof(HasFilteredSongs));
    }

    private void OnSongPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectableSongItemViewModel.IsSelected))
            RefreshSelectionState();
    }

    private void OnFilteredSongsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasFilteredSongs));
    }

    private void RefreshSelectionState()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanExecuteBatchActions));
        AddToQueueCommand.NotifyCanExecuteChanged();
        AddToPlaylistCommand.NotifyCanExecuteChanged();
        PlaySelectedCommand.NotifyCanExecuteChanged();
    }
}

public partial class SelectableSongItemViewModel : ObservableObject
{
    private readonly SongItem _song;

    public SelectableSongItemViewModel(SongItem song)
    {
        _song = song;
    }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public SongItem Song => _song;
    public string Name => _song.Name;
    public string Singer => _song.Singer;
    public string AlbumName => string.IsNullOrWhiteSpace(_song.AlbumName) ? "未知专辑" : _song.AlbumName;
    public string? Cover => _song.Cover;
    public double DurationSeconds => _song.DurationSeconds;
}
