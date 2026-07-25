using System.Collections.Generic;
using System.Threading.Tasks;
using KuGou.Net.Abstractions.Models;
using KugouAvaloniaPlayer.ViewModels;

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
    INavigationService navigationService) : ISongInteractionService
{
    public void NavigateToSinger(SingerLite singer)
    {
        navigationService.NavigateTransient(
            singerViewModelFactory.Create(singer.Id.ToString(), singer.Name));
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
}
