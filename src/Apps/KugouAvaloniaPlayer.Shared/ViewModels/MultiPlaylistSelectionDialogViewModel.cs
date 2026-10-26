using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using ZLinq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class MultiPlaylistSelectionDialogViewModel : ObservableObject
{
    private readonly List<PlaylistDialogPlaylistItemViewModel> _allPlaylists;
    private readonly Action _cancelAction;
    private readonly Func<IReadOnlyList<PlaylistDialogPlaylistItemViewModel>, Task> _confirmAction;

    [ObservableProperty]
    public partial string? FilterText { get; set; }

    [ObservableProperty]
    public partial bool IsSubmitting { get; set; }

    public MultiPlaylistSelectionDialogViewModel(
        int songCount,
        IEnumerable<PlaylistDialogPlaylistItemViewModel> playlists,
        Func<IReadOnlyList<PlaylistDialogPlaylistItemViewModel>, Task> confirmAction,
        Action cancelAction)
    {
        SongCount = songCount;
        _confirmAction = confirmAction;
        _cancelAction = cancelAction;
        _allPlaylists = playlists.AsValueEnumerable().ToList();

        foreach (var playlist in _allPlaylists)
        {
            playlist.PropertyChanged += OnPlaylistPropertyChanged;
            FilteredPlaylists.Add(playlist);
        }
    }

    public int SongCount { get; }
    public ObservableCollection<PlaylistDialogPlaylistItemViewModel> FilteredPlaylists { get; } = new();
    public bool HasPlaylists => _allPlaylists.Count > 0;
    public bool HasFilteredPlaylists => FilteredPlaylists.Count > 0;
    public int SelectedCount => _allPlaylists.AsValueEnumerable().Count(x => x.IsSelected);
    public bool CanConfirm => SelectedCount > 0 && !IsSubmitting;

    partial void OnFilterTextChanged(string? value)
    {
        ApplyFilter(value);
    }

    partial void OnIsSubmittingChanged(bool value)
    {
        RefreshState();
    }

    [RelayCommand]
    private void TogglePlaylistSelection(PlaylistDialogPlaylistItemViewModel? playlist)
    {
        if (playlist is null || IsSubmitting)
            return;

        playlist.IsSelected = !playlist.IsSelected;
        RefreshState();
    }

    [RelayCommand]
    private void SelectAllFiltered()
    {
        if (IsSubmitting)
            return;

        foreach (var playlist in FilteredPlaylists)
            playlist.IsSelected = true;

        RefreshState();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        if (IsSubmitting)
            return;

        foreach (var playlist in _allPlaylists)
            playlist.IsSelected = false;

        RefreshState();
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
        var selectedPlaylists = _allPlaylists.AsValueEnumerable().Where(x => x.IsSelected).ToList();
        if (selectedPlaylists.Count == 0)
            return;

        try
        {
            IsSubmitting = true;
            await _confirmAction(selectedPlaylists);
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
            source = source.AsValueEnumerable().Where(x =>
                x.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        FilteredPlaylists.Clear();
        foreach (var playlist in source)
            FilteredPlaylists.Add(playlist);

        OnPropertyChanged(nameof(HasFilteredPlaylists));
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanConfirm));
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    private void OnPlaylistPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaylistDialogPlaylistItemViewModel.IsSelected))
            RefreshState();
    }
}
