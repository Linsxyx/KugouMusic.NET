using System;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using KugouAvaloniaPlayer.Services;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class HistoryViewModel : PageViewModelBase
{
    private readonly PlaybackHistoryService _historyService;
    private readonly PlayerViewModel _player;

    public HistoryViewModel(PlaybackHistoryService historyService, PlayerViewModel player)
    {
        _historyService = historyService;
        _player = player;
        _historyService.HistoryChanged += OnHistoryChanged;
        _ = LoadAsync();
    }

    public override string DisplayName => "播放历史";
    public override string Icon => "/Assets/history-svgrepo-com.svg";

    public AvaloniaList<SongItem> Songs { get; } = new();
    public string Subtitle => $"{Songs.Count} 首，最多保留 100 首";

    public async Task LoadAsync()
    {
        var songs = await _historyService.LoadSongsAsync();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Songs.Clear();
            Songs.AddRange(songs);
            OnPropertyChanged(nameof(Subtitle));
        });
    }

    [RelayCommand]
    private async Task PlayFirst()
    {
        if (Songs.Count == 0)
            return;

        await _player.PlaySongAsync(Songs[0], Songs);
    }

    [RelayCommand]
    private async Task ClearHistory()
    {
        await _historyService.ClearAsync();
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        _ = LoadAsync();
    }
}
