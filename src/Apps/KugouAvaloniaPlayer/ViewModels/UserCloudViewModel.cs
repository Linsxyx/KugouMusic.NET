using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using KugouAvaloniaPlayer.Models;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;
using ZLinq;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class UserCloudViewModel(
    UserClient userClient,
    ISukiToastManager toastManager,
    ILogger<UserCloudViewModel> logger) : PageViewModelBase
{
    private const int PageSize = 100;
    private const string DefaultCollectionCover = "avares://KugouAvaloniaPlayer/Assets/default_listcard.png";
    private const string DefaultSongCover = "avares://KugouAvaloniaPlayer/Assets/default_song.png";

    private int _currentPage = 1;
    private bool _hasMoreSongs = true;

    [ObservableProperty]
    public partial string Cover { get; set; } = DefaultCollectionCover;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; set; }

    [ObservableProperty]
    public partial string Subtitle { get; set; } = "登录后可查看你的云盘歌曲";

    [ObservableProperty]
    public partial string Title { get; set; } = "我的云盘";

    public override string DisplayName => "用户云盘";
    public override string Icon => "/Assets/cloud-download-svgrepo-com.svg";

    public AvaloniaList<SongItem> Songs { get; } = new();

    [RelayCommand]
    private async Task LoadCloud()
    {
        if (IsLoading)
            return;

        if (!userClient.IsLoggedIn())
        {
            Songs.Clear();
            _currentPage = 1;
            _hasMoreSongs = false;
            Cover = DefaultCollectionCover;
            Subtitle = "登录后可查看你的云盘歌曲";
            return;
        }

        IsLoading = true;
        IsLoadingMore = false;
        Songs.Clear();
        _currentPage = 1;
        _hasMoreSongs = true;

        try
        {
            await LoadPageInternalAsync(resetExisting: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "加载云盘歌曲失败");
            toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("加载云盘失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadMore()
    {
        if (IsLoading || IsLoadingMore || !_hasMoreSongs || !userClient.IsLoggedIn())
            return;

        _currentPage++;
        IsLoadingMore = true;

        try
        {
            await LoadPageInternalAsync(resetExisting: false);
        }
        catch (Exception ex)
        {
            _currentPage = Math.Max(1, _currentPage - 1);
            logger.LogError(ex, "加载更多云盘歌曲失败");
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    private async Task LoadPageInternalAsync(bool resetExisting)
    {
        var data = await userClient.GetCloudAsync(_currentPage, PageSize);
        if (data == null)
        {
            _hasMoreSongs = false;
            if (resetExisting)
            {
                Cover = DefaultCollectionCover;
                Subtitle = "暂时无法读取云盘数据";
            }
            return;
        }

        if (data.Status != 1)
        {
            _hasMoreSongs = false;
            if (resetExisting)
            {
                Cover = DefaultCollectionCover;
                Subtitle = "云盘数据加载失败";
            }
            logger.LogWarning("加载云盘数据失败。status={Status}, errorCode={ErrorCode}", data.Status, data.ErrorCode);
            return;
        }

        Subtitle = BuildSubtitle(data);

        var songItems = data.Songs.AsValueEnumerable().Select(ToSongItem).ToList();
        if (resetExisting)
            Songs.Clear();

        if (songItems.Count > 0)
            Songs.AddRange(songItems);

        if (resetExisting)
        {
            Cover = songItems.AsValueEnumerable()
                        .Select(x => x.Cover)
                        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) &&
                                             !string.Equals(x, DefaultSongCover, StringComparison.OrdinalIgnoreCase))
                    ?? DefaultCollectionCover;
        }

        if (songItems.Count < PageSize || (data.ListCount > 0 && Songs.Count >= data.ListCount))
            _hasMoreSongs = false;
    }

    private static SongItem ToSongItem(UserCloudSong song)
    {
        var singerName = song.Authors.Count > 0
            ? string.Join("、", song.Authors.AsValueEnumerable().Select(x => x.AuthorName).ToArray())
            : string.IsNullOrWhiteSpace(song.AuthorName) ? "未知" : song.AuthorName;

        var singers = song.Authors.AsValueEnumerable().Select(x => new SingerLite
        {
            Id = x.AuthorId,
            Name = x.AuthorName,
            SingerPic = x.Avatar ?? string.Empty
        }).ToList();

        return new SongItem
        {
            Name = song.Name,
            Singer = singerName,
            Hash = song.Hash,
            AudioId = song.AudioId,
            AlbumAudioId = song.AlbumAudioId,
            AlbumName = song.Album?.AlbumName ?? string.Empty,
            Cover = string.IsNullOrWhiteSpace(song.Cover) ? DefaultSongCover : song.Cover,
            DurationSeconds = song.DurationMs / 1000.0,
            Singers = singers,
            PlaybackSource = SongPlaybackSource.UserCloud
        };
    }

    private static string BuildSubtitle(UserCloudResponse response)
    {
        var songPart = response.ListCount > 0 ? $"{response.ListCount} 首歌曲" : "云盘歌曲";
        if (response.MaxSize <= 0)
            return songPart;

        return $"{songPart} · 已用 {FormatBytes(response.UsedSize)} / {FormatBytes(response.MaxSize)}";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = Math.Max(bytes, 0);
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        var format = unitIndex == 0 ? "0" : "0.##";
        return value.ToString(format, CultureInfo.InvariantCulture) + " " + units[unitIndex];
    }
}
