using System;
using System.Collections.Generic;
using ZLinq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KugouAvaloniaPlayer.Converters;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.Jellyfin;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class LocalMusicLibraryViewModel : PageViewModelBase
{
    private const string DefaultSortText = "默认排序";
    private const string ArtistSortText = "按歌手排序";
    private const string AlbumSortText = "按专辑排序";
    private const string DefaultCover = "avares://KugouAvaloniaPlayer/Assets/default_listcard.png";
    private const string DefaultSongCover = "avares://KugouAvaloniaPlayer/Assets/default_song.png";

    private readonly ICreatePlaylistDialogService _createPlaylistDialogService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IJellyfinClient _jellyfinClient;
    private readonly ILocalMusicLibraryService _localMusicLibraryService;
    private readonly ILogger<LocalMusicLibraryViewModel> _logger;
    private readonly ISukiToastManager _toastManager;
    private readonly List<SongItem> _selectedPlaylistSongsDefaultOrder = new();

    [ObservableProperty]
    public partial string CurrentSortText { get; set; }

    [ObservableProperty]
    public partial bool IsImportingJellyfinLibrary { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshingLocalLibrary { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocalLibraryHome))]
    public partial bool IsShowingSongs { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocalPlaylist))]
    [NotifyPropertyChangedFor(nameof(IsLocalLibraryHome))]
    public partial PlaylistItem? SelectedPlaylist { get; set; }

    public LocalMusicLibraryViewModel(
        ICreatePlaylistDialogService createPlaylistDialogService,
        IFolderPickerService folderPickerService,
        IJellyfinClient jellyfinClient,
        ILocalMusicLibraryService localMusicLibraryService,
        ISukiToastManager toastManager,
        ILogger<LocalMusicLibraryViewModel> logger)
    {
        _createPlaylistDialogService = createPlaylistDialogService;
        _folderPickerService = folderPickerService;
        _jellyfinClient = jellyfinClient;
        _localMusicLibraryService = localMusicLibraryService;
        _toastManager = toastManager;
        _logger = logger;
        CurrentSortText = GetSortText(SettingsManager.Settings.LocalPlaylistSongSortMode);

        _ = LoadLocalLibraryAsync();

        WeakReferenceMessenger.Default.Register<SetLocalSongCoverMessage>(this,
            (_, m) => _ = SetLocalSongCoverSafelyAsync(m.Song));
        WeakReferenceMessenger.Default.Register<RemoveFromPlaylistMessage>(this,
            (_, m) => _ = RemoveSongFromPlaylistSafelyAsync(m.Song));
        WeakReferenceMessenger.Default.Register<RefreshPlaylistsMessage>(this,
            (_, _) => _ = LoadLocalLibraryAsync());
    }

    public bool IsLocalPlaylist => SelectedPlaylist?.Type == PlaylistType.Local;
    public bool IsLocalLibraryHome => !IsShowingSongs;

    public override string DisplayName => "本地音乐库";
    public override string Icon => "/Assets/music-folder-8-svgrepo-com.svg";

    public IReadOnlyList<string> SortOptions { get; } = [DefaultSortText, ArtistSortText, AlbumSortText];

    public AvaloniaList<PlaylistItem> LocalLibraryPlaylists { get; } = new();
    public AvaloniaList<SongItem> SelectedPlaylistSongs { get; } = new();

    partial void OnCurrentSortTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            CurrentSortText = GetSortText(SettingsManager.Settings.LocalPlaylistSongSortMode);
            return;
        }

        SettingsManager.Settings.LocalPlaylistSongSortMode = GetSortMode(value);
        SettingsManager.Save();
        ApplySongSort();
    }

    [RelayCommand]
    private void GoBack()
    {
        IsShowingSongs = false;
        SelectedPlaylist = null;
        _selectedPlaylistSongsDefaultOrder.Clear();
        SelectedPlaylistSongs.Clear();
        OnPropertyChanged(nameof(IsLocalLibraryHome));
    }

    [RelayCommand]
    private void BackFromPlaylist()
    {
        GoBack();
    }

    [RelayCommand]
    private async Task LoadLocalLibraryAsync()
    {
        LocalLibraryPlaylists.Clear();

        try
        {
            var localPlaylists = await _localMusicLibraryService.GetPlaylistsAsync();
            LocalLibraryPlaylists.AddRange(localPlaylists.AsValueEnumerable().Select(ToLocalPlaylistItem).ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载本地音乐库失败");
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("本地音乐库加载失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }
    }

    [RelayCommand]
    private async Task OpenPlaylist(PlaylistItem item)
    {
        if (item.Type != PlaylistType.Local)
            return;

        SelectedPlaylist = item;
        IsShowingSongs = true;
        _selectedPlaylistSongsDefaultOrder.Clear();
        SelectedPlaylistSongs.Clear();
        await LoadLocalPlaylistSongsAsync(item);
    }

    private async Task LoadLocalPlaylistSongsAsync(PlaylistItem item)
    {
        if (!long.TryParse(item.Id, out var playlistId))
            return;

        IsLoadingMore = true;
        try
        {
            var tracks = await _localMusicLibraryService.GetPlaylistTracksAsync(playlistId);
            _selectedPlaylistSongsDefaultOrder.Clear();
            _selectedPlaylistSongsDefaultOrder.AddRange(tracks.AsValueEnumerable().Select(ToSongItem).ToArray());
            ApplySongSort();
            item.Count = tracks.Count;
            item.Subtitle = $"{tracks.Count} 首歌曲";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载本地歌单歌曲失败 playlistId={PlaylistId}", item.Id);
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("加载失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    [RelayCommand]
    private async Task DeleteLocalPlaylist(PlaylistItem? item)
    {
        if (item == null || item.Type != PlaylistType.Local)
            return;

        if (!long.TryParse(item.Id, out var playlistId))
            return;

        try
        {
            await _localMusicLibraryService.DeletePlaylistAsync(playlistId);
            LocalLibraryPlaylists.Remove(item);

            if (SelectedPlaylist != null && SelectedPlaylist.Id == item.Id)
            {
                SelectedPlaylist = null;
                _selectedPlaylistSongsDefaultOrder.Clear();
                SelectedPlaylistSongs.Clear();
                IsShowingSongs = false;
            }

            _toastManager.CreateToast()
                .OfType(NotificationType.Success)
                .WithTitle("已删除")
                .WithContent($"已删除本地歌单「{item.Name}」")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除本地歌单失败 playlistId={PlaylistId}", item.Id);
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("删除失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }
    }

    [RelayCommand]
    private async Task EditLocalPlaylist(PlaylistItem? item)
    {
        item ??= SelectedPlaylist;
        if (item?.Type != PlaylistType.Local || !long.TryParse(item.Id, out var playlistId))
            return;

        var result = await _createPlaylistDialogService.PromptLocalPlaylistEditAsync(item.Name, LocalPathFromImageSource(item.Cover));
        if (result == null)
            return;

        await _localMusicLibraryService.UpdatePlaylistAsync(playlistId, result.Name, result.CoverPath);

        item.Name = result.Name;
        item.Cover = GetImageSourceOrDefault(result.CoverPath, DefaultCover);
        item.Subtitle = $"{item.Count} 首歌曲";

        _toastManager.CreateToast()
            .OfType(NotificationType.Success)
            .WithTitle("已保存")
            .WithContent($"已更新本地歌单「{item.Name}」")
            .Dismiss().After(TimeSpan.FromSeconds(3))
            .Dismiss().ByClicking()
            .Queue();
    }

    [RelayCommand]
    private async Task SetLocalSongCover(SongItem? song)
    {
        if (song == null || SelectedPlaylist?.Type != PlaylistType.Local || song.LocalTrackId <= 0)
            return;

        var coverPath = await _folderPickerService.PickSingleImageFileAsync("选择歌曲封面");
        if (string.IsNullOrWhiteSpace(coverPath))
            return;

        await _localMusicLibraryService.SetTrackCoverAsync(song.LocalTrackId, coverPath);
        if (!string.IsNullOrWhiteSpace(song.LocalFilePath))
            song.Cover = LocalImageSourceHelper.BuildEmbeddedCoverSource(song.LocalFilePath);

        _toastManager.CreateToast()
            .OfType(NotificationType.Success)
            .WithTitle("已设置封面")
            .WithContent($"已将封面写入「{song.Name}」的音频标签")
            .Dismiss().After(TimeSpan.FromSeconds(3))
            .Dismiss().ByClicking()
                .Queue();
    }

    [RelayCommand]
    private async Task RemoveSongFromPlaylist(SongItem? song)
    {
        if (song == null || SelectedPlaylist?.Type != PlaylistType.Local || song.LocalTrackId <= 0)
            return;

        if (!long.TryParse(SelectedPlaylist.Id, out var playlistId))
            return;

        await _localMusicLibraryService.RemoveTrackFromPlaylistAsync(playlistId, song.LocalTrackId);

        _selectedPlaylistSongsDefaultOrder.Remove(song);
        SelectedPlaylistSongs.Remove(song);
        if (SelectedPlaylist.Count > 0)
            SelectedPlaylist.Count--;
        SelectedPlaylist.Subtitle = $"{SelectedPlaylist.Count} 首歌曲";

        var sidebarItem = LocalLibraryPlaylists.AsValueEnumerable().FirstOrDefault(x => x.Id == SelectedPlaylist.Id);
        if (sidebarItem != null)
        {
            sidebarItem.Count = SelectedPlaylist.Count;
            sidebarItem.Subtitle = SelectedPlaylist.Subtitle;
        }

        _toastManager.CreateToast()
            .OfType(NotificationType.Success)
            .WithTitle("移除成功")
            .WithContent($"已从歌单移除「{song.Name}」")
            .Dismiss().After(TimeSpan.FromSeconds(3))
            .Dismiss().ByClicking()
            .Queue();
    }

    [RelayCommand]
    private async Task ShowCreateLocalPlaylistDialog()
    {
        var name = await _createPlaylistDialogService.PromptPlaylistNameAsync("新建本地歌单");
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            var playlist = await _localMusicLibraryService.CreatePlaylistAsync(name);
            await LoadLocalLibraryAsync();
            var item = LocalLibraryPlaylists.AsValueEnumerable().FirstOrDefault(x => x.Id == playlist.Id.ToString());
            if (item != null)
                await OpenPlaylist(item);

            _toastManager.CreateToast()
                .OfType(NotificationType.Success)
                .WithTitle("创建成功")
                .WithContent($"已创建本地歌单「{playlist.Name}」")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建本地歌单失败");
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("创建失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }
    }

    [RelayCommand]
    private async Task AddLocalFiles()
    {
        var files = await _folderPickerService.PickAudioFilesAsync("选择本地歌曲");
        if (files.Count == 0)
            return;

        try
        {
            var target = SelectedPlaylist?.Type == PlaylistType.Local ? SelectedPlaylist : LocalLibraryPlaylists
                .AsValueEnumerable().FirstOrDefault();
            if (target == null)
            {
                var playlist = await _localMusicLibraryService.CreatePlaylistAsync("本地歌曲");
                await LoadLocalLibraryAsync();
                target = LocalLibraryPlaylists.AsValueEnumerable().FirstOrDefault(x => x.Id == playlist.Id.ToString());
            }

            if (target == null || !long.TryParse(target.Id, out var playlistId))
                return;

            await _localMusicLibraryService.AddFilesToPlaylistAsync(playlistId, files);
            await LoadLocalLibraryAsync();

            target = LocalLibraryPlaylists.AsValueEnumerable().FirstOrDefault(x => x.Id == playlistId.ToString());
            if (target != null)
                await OpenPlaylist(target);

            _toastManager.CreateToast()
                .OfType(NotificationType.Success)
                .WithTitle("已添加")
                .WithContent($"已添加 {files.Count} 首本地歌曲。")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "添加本地歌曲失败");
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("添加失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }
    }

    [RelayCommand]
    private void OpenLocalLibraryHome()
    {
        IsShowingSongs = false;
        SelectedPlaylist = null;
        _selectedPlaylistSongsDefaultOrder.Clear();
        SelectedPlaylistSongs.Clear();
    }

    [RelayCommand]
    private async Task ImportLocalFolder()
    {
        var path = await _folderPickerService.PickSingleFolderAsync("选择本地音乐文件夹");
        if (string.IsNullOrWhiteSpace(path))
            return;

        IsLoadingMore = true;
        try
        {
            var imported = await _localMusicLibraryService.ImportFolderAsync(path);
            await LoadLocalLibraryAsync();
            var item = LocalLibraryPlaylists.AsValueEnumerable().FirstOrDefault(x => x.Id == imported.Id.ToString());
            if (item != null)
                await OpenPlaylist(item);

            _toastManager.CreateToast()
                .OfType(NotificationType.Success)
                .WithTitle("导入完成")
                .WithContent($"已导入本地歌单「{imported.Name}」，共 {imported.TrackCount} 首。")
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导入本地文件夹失败 path={Path}", path);
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("导入失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    [RelayCommand]
    private async Task RefreshLocalLibrary()
    {
        if (IsRefreshingLocalLibrary)
            return;

        IsRefreshingLocalLibrary = true;
        var progressToast = _toastManager.CreateToast()
            .WithTitle("正在刷新本地音乐库...")
            .WithContent("正在重新扫描已导入的文件夹和 Jellyfin 媒体库。")
            .Queue();
        try
        {
            var refreshed = await _localMusicLibraryService.RefreshImportedLibrariesAsync();
            await LoadLocalLibraryAsync();

            if (SelectedPlaylist?.Type == PlaylistType.Local &&
                long.TryParse(SelectedPlaylist.Id, out var playlistId) &&
                refreshed.AsValueEnumerable().Any(x => x.Id == playlistId))
            {
                await LoadLocalPlaylistSongsAsync(SelectedPlaylist);
            }

            var songCount = refreshed.AsValueEnumerable().Sum(x => x.TrackCount);
            _toastManager.CreateToast()
                .OfType(NotificationType.Success)
                .WithTitle("刷新完成")
                .WithContent(refreshed.Count == 0
                    ? "没有可刷新的导入音乐库。"
                    : $"已刷新 {refreshed.Count} 个歌单，共 {songCount} 首。")
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刷新本地音乐库失败");
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("刷新失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }
        finally
        {
            _toastManager.Dismiss(progressToast);
            IsRefreshingLocalLibrary = false;
        }
    }

    [RelayCommand]
    private async Task ImportJellyfinLibrary()
    {
        if (IsImportingJellyfinLibrary)
            return;

        var lastSettings = GetLastJellyfinSettings();
        var options = await _createPlaylistDialogService.PromptJellyfinConnectionAsync(lastSettings);
        if (options == null)
            return;

        IsImportingJellyfinLibrary = true;
        try
        {
            var libraries = await _jellyfinClient.GetMusicLibrariesAsync(options);
            if (libraries.Count == 0)
            {
                _toastManager.CreateToast()
                    .OfType(NotificationType.Warning)
                    .WithTitle("未找到音乐库")
                    .WithContent("当前 Jellyfin 用户没有可导入的音乐媒体库。")
                    .Dismiss().After(TimeSpan.FromSeconds(4))
                    .Dismiss().ByClicking()
                    .Queue();
                return;
            }

            SaveJellyfinSettings(options);

            var library = await _createPlaylistDialogService.PromptJellyfinLibraryAsync(libraries);
            if (library == null)
                return;

            var progressBar = new ProgressBar
            {
                Value = 0,
                Minimum = 0,
                Maximum = 100,
                ShowProgressText = true
            };
            var progressText = new TextBlock { Text = "准备导入..." };
            var progressContent = new StackPanel
            {
                Spacing = 8,
                Children = { progressText, progressBar }
            };

            var progressToast = _toastManager.CreateToast()
                .WithTitle("正在导入 Jellyfin...")
                .WithContent(progressContent)
                .Queue();

            var progressReporter = new Progress<JellyfinImportProgress>(p =>
            {
                progressBar.Value = p.Percentage;
                progressText.Text = p.Message;
            });

            IReadOnlyList<LocalPlaylistSummary> imported;
            try
            {
                imported = await _localMusicLibraryService.ImportJellyfinLibraryAsync(
                    options,
                    library,
                    progressReporter);
            }
            finally
            {
                _toastManager.Dismiss(progressToast);
            }

            await LoadLocalLibraryAsync();
            var firstImported = imported.AsValueEnumerable().FirstOrDefault();
            var item = firstImported == null
                ? null
                : LocalLibraryPlaylists.AsValueEnumerable().FirstOrDefault(x => x.Id == firstImported.Id.ToString());
            if (item != null)
                await OpenPlaylist(item);

            var importedSongCount = imported.AsValueEnumerable().Sum(x => x.TrackCount);
            _toastManager.CreateToast()
                .OfType(NotificationType.Success)
                .WithTitle("导入完成")
                .WithContent($"已按专辑同步 Jellyfin 媒体库「{library.Name}」，生成 {imported.Count} 个本地歌单，共 {importedSongCount} 首。")
                .Dismiss().After(TimeSpan.FromSeconds(5))
                .Dismiss().ByClicking()
                .Queue();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导入 Jellyfin 媒体库失败");
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("导入失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(5))
                .Dismiss().ByClicking()
                .Queue();
        }
        finally
        {
            IsImportingJellyfinLibrary = false;
        }
    }

    private async Task SetLocalSongCoverSafelyAsync(SongItem? song)
    {
        try
        {
            await SetLocalSongCover(song);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理设置本地歌曲封面消息失败");
        }
    }

    private async Task RemoveSongFromPlaylistSafelyAsync(SongItem? song)
    {
        try
        {
            await RemoveSongFromPlaylist(song);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理从本地歌单移除歌曲消息失败");
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("移除失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }
    }

    private static PlaylistItem ToLocalPlaylistItem(LocalPlaylistSummary item)
    {
        return new PlaylistItem
        {
            Name = item.Name,
            Id = item.Id.ToString(),
            Count = item.TrackCount,
            Type = PlaylistType.Local,
            Cover = GetImageSourceOrDefault(item.CoverPath, DefaultCover),
            Subtitle = $"{item.TrackCount} 首歌曲"
        };
    }

    private static SongItem ToSongItem(LocalTrackItem item)
    {
        var isJellyfinTrack = string.Equals(
            item.SourceType,
            LocalMusicLibraryService.SourceTypeJellyfin,
            StringComparison.Ordinal);

        return new SongItem
        {
            LocalTrackId = item.Id,
            Name = item.Title,
            Singer = item.Artist,
            AlbumName = item.Album,
            DurationSeconds = item.DurationSeconds,
            LocalSourceType = item.SourceType,
            LocalFilePath = item.LocalPath,
            RemoteUrl = item.RemoteUrl,
            Cover = isJellyfinTrack
                ? string.IsNullOrWhiteSpace(item.CoverPath) ? DefaultSongCover : item.CoverPath
                : ResolveLocalSongCoverSource(item.CoverPath, item.LocalPath)
        };
    }

    private static JellyfinServerSettings? GetLastJellyfinSettings()
    {
        var fingerprint = SettingsManager.Settings.LastJellyfinServerFingerprint;
        if (string.IsNullOrWhiteSpace(fingerprint))
            return null;

        return SettingsManager.Settings.JellyfinServers.TryGetValue(fingerprint, out var settings)
            ? settings
            : null;
    }

    private void SaveJellyfinSettings(JellyfinConnectionOptions options)
    {
        var fingerprint = _jellyfinClient.GetServerFingerprint(options.ServerUrl);
        SettingsManager.Settings.JellyfinServers[fingerprint] = new JellyfinServerSettings
        {
            ServerUrl = options.ServerUrl.Trim().TrimEnd('/'),
            UserId = options.UserId.Trim(),
            ApiKey = options.ApiKey.Trim()
        };
        SettingsManager.Settings.LastJellyfinServerFingerprint = fingerprint;
        SettingsManager.Save();
    }

    private void ApplySongSort()
    {
        IEnumerable<SongItem> sortedSongs = GetSortMode(CurrentSortText) switch
        {
            PlaylistSongSortMode.Artist => _selectedPlaylistSongsDefaultOrder
                .AsValueEnumerable().OrderBy(song => song.Singer, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            PlaylistSongSortMode.Album => _selectedPlaylistSongsDefaultOrder
                .AsValueEnumerable().OrderBy(song => song.AlbumName, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            _ => _selectedPlaylistSongsDefaultOrder
        };

        SelectedPlaylistSongs.Clear();
        SelectedPlaylistSongs.AddRange(sortedSongs);
    }

    private static PlaylistSongSortMode GetSortMode(string? value)
    {
        return value switch
        {
            ArtistSortText => PlaylistSongSortMode.Artist,
            AlbumSortText => PlaylistSongSortMode.Album,
            _ => PlaylistSongSortMode.Default
        };
    }

    private static string GetSortText(PlaylistSongSortMode mode)
    {
        return mode switch
        {
            PlaylistSongSortMode.Artist => ArtistSortText,
            PlaylistSongSortMode.Album => AlbumSortText,
            _ => DefaultSortText
        };
    }

    private static string GetImageSourceOrDefault(string? imagePath, string defaultSource)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return defaultSource;

        if (LocalImageSourceHelper.TryGetEmbeddedCoverFilePath(imagePath, out _))
            return imagePath;

        if (Uri.TryCreate(imagePath, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == "avares"))
        {
            return imagePath;
        }

        if (!System.IO.File.Exists(imagePath))
            return defaultSource;

        return new Uri(imagePath).AbsoluteUri;
    }

    private static string ResolveLocalSongCoverSource(string? coverPath, string? localPath)
    {
        if (!string.IsNullOrWhiteSpace(coverPath))
        {
            if (LocalImageSourceHelper.TryGetEmbeddedCoverFilePath(coverPath, out _))
                return coverPath;

            if (System.IO.File.Exists(coverPath))
                return new Uri(coverPath).AbsoluteUri;
        }

        return string.IsNullOrWhiteSpace(localPath) || !System.IO.File.Exists(localPath)
            ? DefaultSongCover
            : LocalImageSourceHelper.BuildEmbeddedCoverSource(localPath);
    }

    private static string? LocalPathFromImageSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source) || !Uri.TryCreate(source, UriKind.Absolute, out var uri) || !uri.IsFile)
            return null;

        return uri.LocalPath;
    }
}
