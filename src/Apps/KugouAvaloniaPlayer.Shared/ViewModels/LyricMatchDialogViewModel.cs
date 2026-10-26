using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Abstractions.Models;
using KugouAvaloniaPlayer.Services;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class LyricMatchDialogViewModel : ObservableObject
{
    private readonly Action<LocalLyricMatchResult> _confirmAction;
    private readonly Action _cancelAction;

    public string Title { get; }
    public List<SongInfo> Results { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseTemporaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(EmbedCommand))]
    public partial SongInfo? SelectedSong { get; set; }

    public LyricMatchDialogViewModel(
        SongItem localSong,
        List<SongInfo> results,
        Action<LocalLyricMatchResult> confirmAction,
        Action cancelAction)
    {
        Title = $"在线匹配歌词 - {localSong.DisplayTitle}";
        Results = results;
        _confirmAction = confirmAction;
        _cancelAction = cancelAction;
    }

    private bool CanConfirm() => SelectedSong is not null;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void UseTemporary()
    {
        if (SelectedSong is { } song)
            _confirmAction(new LocalLyricMatchResult(song, LocalLyricMatchAction.Temporary));
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Embed()
    {
        if (SelectedSong is { } song)
            _confirmAction(new LocalLyricMatchResult(song, LocalLyricMatchAction.Embed));
    }

    [RelayCommand]
    private void Cancel() => _cancelAction();
}
