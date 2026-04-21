using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class AddToPlaylistDialogViewModel : ObservableObject
{
    private readonly List<PlaylistDialogPlaylistItemViewModel> _allPlaylists;
    private readonly Func<PlaylistDialogPlaylistItemViewModel, Task> _confirmAction;
    private readonly Action _cancelAction;

    [ObservableProperty] private string? _filterText;
    [ObservableProperty] private bool _isSubmitting;
    [ObservableProperty] private PlaylistDialogPlaylistItemViewModel? _selectedPlaylist;

    public AddToPlaylistDialogViewModel(
        string songName,
        string songSinger,
        string? songCover,
        IEnumerable<PlaylistDialogPlaylistItemViewModel> playlists,
        Func<PlaylistDialogPlaylistItemViewModel, Task> confirmAction,
        Action cancelAction)
    {
        SongName = songName;
        SongSinger = songSinger;
        SongCover = songCover;
        _confirmAction = confirmAction;
        _cancelAction = cancelAction;
        _allPlaylists = playlists.ToList();

        foreach (var playlist in _allPlaylists)
            FilteredPlaylists.Add(playlist);
    }

    public string SongName { get; }
    public string SongSinger { get; }
    public string? SongCover { get; }

    public ObservableCollection<PlaylistDialogPlaylistItemViewModel> FilteredPlaylists { get; } = new();

    public bool HasPlaylists => _allPlaylists.Count > 0;
    public bool HasFilteredPlaylists => FilteredPlaylists.Count > 0;
    public bool CanConfirm => SelectedPlaylist is not null && !IsSubmitting;

    partial void OnFilterTextChanged(string? value)
    {
        ApplyFilter(value);
    }

    partial void OnSelectedPlaylistChanged(PlaylistDialogPlaylistItemViewModel? value)
    {
        foreach (var playlist in _allPlaylists)
            playlist.IsSelected = ReferenceEquals(playlist, value);

        OnPropertyChanged(nameof(CanConfirm));
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSubmittingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanConfirm));
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SelectPlaylist(PlaylistDialogPlaylistItemViewModel? playlist)
    {
        if (playlist is null || IsSubmitting)
            return;

        SelectedPlaylist = playlist;
    }

    [RelayCommand]
    private void Cancel()
    {
        if (IsSubmitting)
            return;

        _cancelAction();
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        if (SelectedPlaylist is null)
            return;

        try
        {
            IsSubmitting = true;
            await _confirmAction(SelectedPlaylist);
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    private void ApplyFilter(string? filter)
    {
        var keyword = filter?.Trim();
        IEnumerable<PlaylistDialogPlaylistItemViewModel> source = _allPlaylists;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            source = source.Where(x =>
                x.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        FilteredPlaylists.Clear();
        foreach (var playlist in source)
            FilteredPlaylists.Add(playlist);

        OnPropertyChanged(nameof(HasFilteredPlaylists));
    }
}

public partial class PlaylistDialogPlaylistItemViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public required string Name { get; init; }
    public required string ListId { get; init; }
    public required string Cover { get; init; }
    public int SongCount { get; init; }
    public bool IsLikedPlaylist { get; init; }

    public string Subtitle => SongCount > 0 ? $"{SongCount} 首歌曲" : "空歌单";
    public string BadgeText => IsLikedPlaylist ? "我喜欢" : "歌单";
}
