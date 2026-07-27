using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using KugouAvaloniaPlayer.ViewModels;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.Services;

public interface ISongInteractionService
{
    void NavigateToSinger(SingerLite singer);

    Task ShowAddToPlaylistDialogAsync(SongItem song);

    Task ShowBatchActionsAsync(
        IReadOnlyList<SongItem> songs,
        bool allowAddToPlaylist = true);
}

public sealed class SongInteractionService(
    FavoritePlaylistService favoritePlaylistService,
    IPlaybackCommands playbackCommands,
    ISingerViewModelFactory singerViewModelFactory,
    INavigationService navigationService,
    SearchClient searchClient,
    ILocalSingerMatchService localSingerMatchService,
    ISukiToastManager toastManager) : ISongInteractionService {
    
    public void NavigateToSinger(SingerLite singer) {
        if (singer.Id == -1 && !string.IsNullOrWhiteSpace(singer.Name))
            MatchLocalSingersAsync(singer)
                .ContinueWith(result => {
                    if (!result.IsCompletedSuccessfully || result.Result is not { } matched)
                        return;
                    navigationService.NavigateTransient(
                        singerViewModelFactory.Create(matched.Id.ToString(), matched.Name));
                });
        else
            navigationService.NavigateTransient(singerViewModelFactory.Create(singer.Id.ToString(), singer.Name));
    }

    public Task ShowAddToPlaylistDialogAsync(SongItem song) =>
        favoritePlaylistService.ShowAddToPlaylistDialogAsync(song);

    public Task ShowBatchActionsAsync(
        IReadOnlyList<SongItem> songs,
        bool allowAddToPlaylist = true) =>
        favoritePlaylistService.ShowSongBatchActionDialogAsync(
            songs,
            playbackCommands,
            allowAddToPlaylist);
    
    public async Task<SingerLite?> MatchLocalSingersAsync(SingerLite? singer) {
        if (singer is null || string.IsNullOrWhiteSpace(singer.Name)) {
            toastManager.CreateToast()
                        .OfType(NotificationType.Warning)
                        .WithTitle("搜索失败")
                        .WithContent("该歌手不存在")
                        .Dismiss()
                        .ByClicking()
                        .Dismiss()
                        .After(TimeSpan.FromSeconds(3))
                        .Queue();
            return null;
        }

        // 搜索在线匹配的歌手
        var results = await searchClient.SearchAuthorAsync(singer.Name, pageSize: 30);
        if (results == null || results.Count == 0) {
            toastManager.CreateToast()
                        .OfType(NotificationType.Warning)
                        .WithTitle("搜索失败")
                        .WithContent("未找到匹配的歌手")
                        .Dismiss()
                        .ByClicking()
                        .Dismiss()
                        .After(TimeSpan.FromSeconds(3))
                        .Queue();
            return null;
        }

        // 弹窗让用户选择
        return await localSingerMatchService.MatchSingerAsync(singer, results);
    }
}
