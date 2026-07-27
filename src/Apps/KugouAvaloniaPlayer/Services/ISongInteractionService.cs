using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    Task MatchLocalLyricsAsync(SongItem song);
}

public sealed class SongInteractionService(
    FavoritePlaylistService favoritePlaylistService,
    IPlaybackCommands playbackCommands,
    ISingerViewModelFactory singerViewModelFactory,
    INavigationService navigationService,
    SearchClient searchClient,
    ILocalSingerMatchService localSingerMatchService,
    ILocalLyricMatchService localLyricMatchService,
    LyricsService lyricsService,
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

    public async Task MatchLocalLyricsAsync(SongItem song)
    {
        if (string.IsNullOrWhiteSpace(song.LocalFilePath) ||
            !File.Exists(song.LocalFilePath))
        {
            ShowToast(NotificationType.Warning, "匹配失败", "只有本地音频文件支持歌词匹配");
            return;
        }

        try
        {
            var keyword = string.Join(' ', new[] { song.DisplayTitle, song.Singer }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            var results = await searchClient.SearchAsync(keyword, pageSize: 30);
            if (results.Count == 0)
            {
                ShowToast(NotificationType.Warning, "搜索失败", "未找到匹配的在线歌曲");
                return;
            }

            var selection = await localLyricMatchService.MatchLyricAsync(song, results);
            if (selection is null)
                return;

            if (selection.Action == LocalLyricMatchAction.Embed && IsReadOnly(song.LocalFilePath))
            {
                ShowToast(
                    NotificationType.Error,
                    "内嵌失败",
                    $"文件没有写权限，请修改权限后重试：{Path.GetFileName(song.LocalFilePath)}");
                return;
            }

            var lyric = await lyricsService.DownloadOnlineLyricAsync(selection.Song.Hash, selection.Song.Name);
            if (lyric is null)
            {
                ShowToast(NotificationType.Warning, "匹配失败", "所选歌曲没有可用歌词");
                return;
            }

            if (selection.Action == LocalLyricMatchAction.Temporary)
            {
                song.TemporaryLyricHash = selection.Song.Hash;
                song.TemporaryLyricName = selection.Song.Name;
                await playbackCommands.ReloadLyricsAsync(song);
                ShowToast(NotificationType.Success, "匹配成功", "本次运行将使用所选在线歌词");
                return;
            }

            if (IsCurrentPlayingLocalFile(song.LocalFilePath))
            {
                ShowToast(
                    NotificationType.Warning,
                    "内嵌失败",
                    "当前播放的歌曲不能写入内嵌歌词，请切换到其他歌曲后重试");
                return;
            }

            await LyricsService.EmbedLyricsAsync(song.LocalFilePath, lyric);
            song.TemporaryLyricHash = "";
            song.TemporaryLyricName = "";
            await playbackCommands.ReloadLyricsAsync(song);
            ShowToast(NotificationType.Success, "写入成功", "歌词已内嵌到本地音频文件");
        }
        catch (Exception ex) when (IsPermissionDenied(ex))
        {
            ShowToast(
                NotificationType.Error,
                "内嵌失败",
                $"文件没有写权限，请修改权限后重试：{Path.GetFileName(song.LocalFilePath)}");
        }
        catch (Exception ex)
        {
            ShowToast(NotificationType.Error, "歌词匹配失败", ex.Message);
        }
    }

    private bool IsCurrentPlayingLocalFile(string filePath)
    {
        var currentFilePath = playbackCommands.CurrentPlayingSong?.LocalFilePath;
        if (string.IsNullOrWhiteSpace(currentFilePath))
            return false;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.GetFullPath(filePath),
            Path.GetFullPath(currentFilePath),
            comparison);
    }

    private static bool IsReadOnly(string filePath)
    {
        if (OperatingSystem.IsWindows())
            return (File.GetAttributes(filePath) & FileAttributes.ReadOnly) != 0;

        return (File.GetUnixFileMode(filePath) & UnixFileMode.UserWrite) == 0;
    }

    private static bool IsPermissionDenied(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is UnauthorizedAccessException ||
                current.Message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void ShowToast(NotificationType type, string title, string content)
    {
        toastManager.CreateToast()
            .OfType(type)
            .WithTitle(title)
            .WithContent(content)
            .Dismiss()
            .ByClicking()
            .Dismiss()
            .After(TimeSpan.FromSeconds(3))
            .Queue();
    }
}
