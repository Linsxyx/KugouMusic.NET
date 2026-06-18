using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using ZLinq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KuGou.Net.Clients;
using KuGou.Net.Protocol.Session;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using Microsoft.Extensions.Logging;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private static readonly Lock CustomBackgroundImageSync = new();
    private static Bitmap? _sCachedCustomBackgroundBitmap;
    private static string? _sCachedCustomBackgroundPath;

    private readonly LoginClient _authClient;
    private readonly IAppUpdateService _appUpdateService;
    private readonly IDesktopLyricWindowService _desktopLyricWindowService;
    private readonly DailyRecommendViewModel _dailyRecommendViewModel;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly ILoginDialogService _loginDialogService;
    private readonly INavigationService _navigationService;
    private readonly SearchViewModel _searchViewModel;
    private readonly UserCloudViewModel _userCloudViewModel;
    private readonly KgSessionManager _sessionManager;
    private readonly UserClient _userClient;
    private readonly UserViewModel _userViewModel;

    [ObservableProperty]
    public partial PageViewModelBase ActivePage { get; set; }

    [ObservableProperty]
    public partial PageViewModelBase? SelectedMenuPage { get; set; }

    [ObservableProperty]
    public partial bool IsDesktopLyricEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsLoggedIn { get; set; }

    [ObservableProperty]
    public partial bool IsCreatedPlaylistsExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool IsCollectedPlaylistsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsAlbumPlaylistsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsMiniPlayerOpaque { get; set; } = true;

    private bool _isUpdatingActivePageFromNavigation;
    private bool _isUpdatingSelectedMenuPageFromNavigation;
    private bool _isClosingDesktopLyricForShutdown;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial string SearchKeyword { get; set; } = "";

    [ObservableProperty]
    public partial string? UserAvatar { get; set; }

    [ObservableProperty]
    public partial string UserName { get; set; } = "未登录";

    public MainWindowViewModel(
        ISukiToastManager toastManager,
        PlayerViewModel player,
        ISukiDialogManager dialogManager,
        KgSessionManager sessionManager,
        LoginClient authClient,
        UserClient userClient,
        ISingerViewModelFactory singerViewModelFactory,
        IAppUpdateService appUpdateService,
        IDesktopLyricWindowService desktopLyricWindowService,
        ILoginDialogService loginDialogService,
        INavigationService navigationService,
        NowPlayingViewModel nowPlaying,
        LoginViewModel loginViewModel,
        SearchViewModel searchViewModel,
        UserCloudViewModel userCloudViewModel,
        UserViewModel userViewModel,
        RankViewModel rankViewModel,
        DailyRecommendViewModel dailyRecommendViewModel,
        HistoryViewModel historyViewModel,
        DiscoverViewModel discoverViewModel,
        LocalMusicLibraryViewModel localMusicLibraryViewModel,
        MyPlaylistsViewModel myPlaylistsViewModel,
        ILogger<MainWindowViewModel> logger)
    {
        DialogManager = dialogManager;
        _sessionManager = sessionManager;
        _authClient = authClient;
        _dailyRecommendViewModel = dailyRecommendViewModel;
        _userClient = userClient;
        var singerViewModelFactory1 = singerViewModelFactory;
        _appUpdateService = appUpdateService;
        _desktopLyricWindowService = desktopLyricWindowService;
        _loginDialogService = loginDialogService;
        _navigationService = navigationService;

        LoginViewModel = loginViewModel;
        _searchViewModel = searchViewModel;
        _userCloudViewModel = userCloudViewModel;
        _userViewModel = userViewModel;
        PlaylistsViewModel = myPlaylistsViewModel;
        _logger = logger;

        _userViewModel.CheckForUpdateRequested += OnCheckForUpdateRequested;
        _desktopLyricWindowService.IsOpenChanged += OnDesktopLyricWindowStateChanged;

        Player = player;
        NowPlaying = nowPlaying;
        ToastManager = toastManager;

        Pages.Add(_dailyRecommendViewModel);
        Pages.Add(historyViewModel);
        Pages.Add(localMusicLibraryViewModel);
        Pages.Add(discoverViewModel);
        Pages.Add(rankViewModel);
        Pages.Add(_searchViewModel);
        Pages.Add(_userCloudViewModel);
        _navigationService.CurrentPageChanged += OnNavigationCurrentPageChanged;
        _navigationService.NavigateRoot(_dailyRecommendViewModel);
        ActivePage = _dailyRecommendViewModel;
        SelectedMenuPage = _dailyRecommendViewModel;
        IsDesktopLyricEnabled = _desktopLyricWindowService.IsOpen;
        ApplyCustomBackgroundImage(
            SettingsManager.Settings.UseCustomBackgroundImage,
            SettingsManager.Settings.CustomBackgroundImagePath,
            SettingsManager.Settings.CustomBackgroundImageOpacity);

        PlaylistsViewModel.Items.CollectionChanged += OnPlaylistItemsChanged;
        PlaylistsViewModel.PropertyChanged += OnPlaylistViewModelPropertyChanged;
        RefreshSidebarPlaylists();

        WeakReferenceMessenger.Default.Register<PlaySongMessage>(this,
            (_, m) => _ = HandlePlaySongMessageAsync(m.Song));

        WeakReferenceMessenger.Default.Register<NavigateToSingerMessage>(this, (_, m) =>
        {
            NowPlaying.CloseCommand.Execute(null);
            var singerVm = singerViewModelFactory1.Create(m.Singer.Id.ToString(), m.Singer.Name);
            _navigationService.NavigateTransient(singerVm);
        });

        WeakReferenceMessenger.Default.Register<AuthStateChangedMessage>(this, (_, m) =>
        {
            if (m.IsLoggedIn)
                _ = OnLoginSuccessAsync();
            else
                OnLogoutRequested();
        });

        WeakReferenceMessenger.Default.Register<RequestNavigateBackMessage>(this, (_, _) => { NavigateBack(); });
        WeakReferenceMessenger.Default.Register<AppBackgroundSettingsChangedMessage>(this, (_, message) =>
        {
            ApplyCustomBackgroundImage(
                message.UseCustomImage,
                message.CustomImagePath,
                message.CustomImageOpacity);
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await LoadLocalSessionOrLogin();
                await GetDailyRecommendations();
                if (SettingsManager.Settings.AutoCheckUpdate) await _appUpdateService.CheckForUpdatesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动后台初始化任务失败");
            }
        });

        _ = ApplyDeferredStartupPreferencesAsync();
    }

    private async Task ApplyDeferredStartupPreferencesAsync()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(800));
        await Player.RestoreCachedPlaybackQueueAsync();

        Dispatcher.UIThread.Post(() =>
        {
            var savedPlaybackMode = SettingsManager.Settings.PlaybackMode;
            if (savedPlaybackMode != PlayMode.Normal)
                Player.ApplySavedPlaybackModePreference();

            if (SettingsManager.Settings.OpenDesktopLyricOnStartup && !_desktopLyricWindowService.IsOpen)
                _desktopLyricWindowService.Toggle();
        });
    }

    private void ApplyCustomBackgroundImage(
        bool useCustomImage,
        string? customImagePath,
        double customImageOpacity)
    {
        var hasCustomImage = UpdateCustomBackgroundBrush(useCustomImage, customImagePath, Math.Clamp(customImageOpacity, 0.1, 1.0));
        IsMiniPlayerOpaque = !hasCustomImage;
    }

    private static bool UpdateCustomBackgroundBrush(bool useCustomImage, string? path, double opacity)
    {
        var brush = CreateCustomBackgroundBrush(useCustomImage, path, opacity, out var hasCustomImage);
        Dispatcher.UIThread.Post(() =>
        {
            if (Avalonia.Application.Current is not { } app) return;
            app.Resources["KugouCustomBackgroundBrush"] = brush;
            app.Resources["KugouHeroBackgroundImageVisible"] = !hasCustomImage;
        });

        return hasCustomImage;
    }

    private static IBrush CreateCustomBackgroundBrush(
        bool useCustomImage,
        string? path,
        double opacity,
        out bool hasCustomImage)
    {
        hasCustomImage = false;

        if (!useCustomImage || string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            ReleaseCachedCustomBackgroundBitmap();
            return Brushes.Transparent;
        }

        try
        {
            var brush = new ImageBrush(GetOrCreateCustomBackgroundBitmap(path))
            {
                Stretch = Stretch.UniformToFill,
                Opacity = opacity
            };
            hasCustomImage = true;
            return brush;
        }
        catch
        {
            ReleaseCachedCustomBackgroundBitmap(path);
            return Brushes.Transparent;
        }
    }

    private static Bitmap GetOrCreateCustomBackgroundBitmap(string path)
    {
        lock (CustomBackgroundImageSync)
        {
            if (_sCachedCustomBackgroundBitmap is not null &&
                string.Equals(_sCachedCustomBackgroundPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return _sCachedCustomBackgroundBitmap;
            }

            _sCachedCustomBackgroundBitmap?.Dispose();
            _sCachedCustomBackgroundBitmap = new Bitmap(path);
            _sCachedCustomBackgroundPath = path;
            return _sCachedCustomBackgroundBitmap;
        }
    }

    private static void ReleaseCachedCustomBackgroundBitmap(string? path = null)
    {
        lock (CustomBackgroundImageSync)
        {
            if (path is not null &&
                !string.Equals(_sCachedCustomBackgroundPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _sCachedCustomBackgroundBitmap?.Dispose();
            _sCachedCustomBackgroundBitmap = null;
            _sCachedCustomBackgroundPath = null;
        }
    }

    //public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public PlayerViewModel Player { get; }
    public NowPlayingViewModel NowPlaying { get; }
    public ISukiToastManager ToastManager { get; }
    public ISukiDialogManager DialogManager { get; }

    public AvaloniaList<PageViewModelBase> Pages { get; } = new();

    private LoginViewModel LoginViewModel { get; }
    public MyPlaylistsViewModel PlaylistsViewModel { get; }

    public AvaloniaList<PlaylistItem> SidebarCreatedPlaylists { get; } = new();
    public AvaloniaList<PlaylistItem> SidebarCollectedPlaylists { get; } = new();
    public AvaloniaList<PlaylistItem> SidebarAlbumPlaylists { get; } = new();

    private void OnDesktopLyricWindowStateChanged(bool isOpen)
    {
        if (!_isClosingDesktopLyricForShutdown)
        {
            SettingsManager.Settings.OpenDesktopLyricOnStartup = isOpen;
            SettingsManager.Save();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            IsDesktopLyricEnabled = isOpen;
            return;
        }

        Dispatcher.UIThread.Post(() => IsDesktopLyricEnabled = isOpen);
    }

    partial void OnIsCreatedPlaylistsExpandedChanged(bool value)
    {
        if (!value)
            return;

        IsCollectedPlaylistsExpanded = false;
        IsAlbumPlaylistsExpanded = false;
    }

    partial void OnIsCollectedPlaylistsExpandedChanged(bool value)
    {
        if (!value)
            return;

        IsCreatedPlaylistsExpanded = false;
        IsAlbumPlaylistsExpanded = false;
    }

    partial void OnIsAlbumPlaylistsExpandedChanged(bool value)
    {
        if (!value)
            return;

        IsCreatedPlaylistsExpanded = false;
        IsCollectedPlaylistsExpanded = false;
    }

    partial void OnActivePageChanged(PageViewModelBase value)
    {
        if (_isUpdatingActivePageFromNavigation)
            return;

        NavigateToPage(value);
    }

    partial void OnSelectedMenuPageChanged(PageViewModelBase? value)
    {
        if (_isUpdatingSelectedMenuPageFromNavigation || value == null)
            return;

        NavigateToPage(value);
    }

    private void OnNavigationCurrentPageChanged(PageViewModelBase? page)
    {
        if (page == null)
            return;

        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyNavigationPage(page);
            return;
        }

        Dispatcher.UIThread.Post(() => ApplyNavigationPage(page));
    }

    private void ApplyNavigationPage(PageViewModelBase page)
    {
        _isUpdatingActivePageFromNavigation = true;
        ActivePage = page;
        _isUpdatingActivePageFromNavigation = false;

        if (!Pages.Contains(page))
            return;

        _isUpdatingSelectedMenuPageFromNavigation = true;
        SelectedMenuPage = page;
        _isUpdatingSelectedMenuPageFromNavigation = false;
    }

    private void NavigateToPage(PageViewModelBase page)
    {
        if (_navigationService.CurrentPage == page)
            return;

        if (Pages.Contains(page))
        {
            _navigationService.NavigateRoot(page);
            return;
        }

        _navigationService.Navigate(page);
    }

    // --- 登录相关 ---
    private async Task LoadLocalSessionOrLogin()
    {
        try
        {
            var session = _sessionManager.Session;
            if (!string.IsNullOrEmpty(session.Token))
            {
                IsLoggedIn = true;
                await LoadUserInfo();
                _logger.LogInformation("已加载本地用户: {session.UserId}" , session.UserId);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await TryGetVip();
                        await Player.LoadLikeListAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "获取VIP或喜欢列表失败");
                    }
                });
            }
            else
            {
                _logger.LogInformation("未登录，以游客身份运行。");
                _authClient.LogOutAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "登录初始化失败");
            _authClient.LogOutAsync();
        }
    }

    private async Task LoadUserInfo()
    {
        try
        {
            var userInfo = await _userClient.GetUserInfoAsync();
            if (userInfo != null)
            {
                UserName = userInfo.Name;
                UserAvatar = string.IsNullOrWhiteSpace(userInfo.Pic) ? null : userInfo.Pic;
                _userViewModel.UserName = UserName;
                _userViewModel.UserAvatar = UserAvatar;
                _userViewModel.UserId = _sessionManager.Session.UserId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载用户信息失败");
            ToastManager.CreateToast()
                .OfType(NotificationType.Warning)
                .WithTitle("加载用户失败")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
    }

    private async Task TryGetVip()
    {
        var history = await _userClient.GetVipRecordAsync();
        if (history is { Status: 1 })
        {
            var todayStr = DateTime.Now.ToString("yyyy-MM-dd");
            var todayRecord = history.Items.AsValueEnumerable().FirstOrDefault(x => x.Day == todayStr);
            switch (todayRecord)
            {
                case null:
                {
                    var data = await _userClient.ReceiveOneDayVipAsync();
                    if (data is not null && data.Status == 1)
                        _logger.LogInformation("vip领取成功");
                    else
                        _logger.LogError("vip领取失败{data.ErrorCode}" , data?.ErrorCode);
                    await Task.Delay(1000);
                    await _userClient.UpgradeVipRewardAsync();
                    break;
                }
                case { VipType: "tvip" }:
                    await _userClient.UpgradeVipRewardAsync();
                    break;
                default:
                    _logger.LogInformation("今日已领取vip");
                    break;
            }
        }
    }

    private async Task OnLoginSuccessAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            DialogManager.DismissDialog();
            IsLoggedIn = true;
        });

        await LoadUserInfo();
        _logger.LogInformation("登录成功");

        if (ReferenceEquals(ActivePage, _userCloudViewModel))
            await _userCloudViewModel.LoadCloudCommand.ExecuteAsync(null);

        _ = Task.Run(async () =>
        {
            try
            {
                await TryGetVip();
                await Player.LoadLikeListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化VIP或喜欢列表失败");
            }
        });

        await GetDailyRecommendations();
    }

    private void OnLogoutRequested()
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsLoggedIn = false;
            UserName = "未登录";
            UserAvatar = null;
            _userViewModel.UserName = UserName;
            _userViewModel.UserAvatar = null;
            _userViewModel.UserId = string.Empty;
            _userViewModel.VipStatus = "未开通";
            Player.ClearPersonalFmSession();
            _userCloudViewModel.LoadCloudCommand.Execute(null);
            _ = _dailyRecommendViewModel.OnAuthStateChangedAsync();
            _logger.LogInformation("已退出登录");

            // 返回每日推荐页面
            _navigationService.NavigateRoot(_dailyRecommendViewModel);
        });
    }

    private void OnCheckForUpdateRequested()
    {
        _ = CheckForUpdatesFromUserAsync();
    }

    private async Task CheckForUpdatesFromUserAsync()
    {
        try
        {
            await _appUpdateService.CheckForUpdatesAsync(true);
        }
        finally
        {
            Dispatcher.UIThread.Post(() => _userViewModel.SetCheckingUpdateState(false));
        }
    }

    [RelayCommand]
    private void ShowLoginDialog()
    {
        _loginDialogService.ShowLoginDialog(LoginViewModel);
    }

    [RelayCommand]
    private void NavigateToUser()
    {
        if (IsLoggedIn)
            _ = _userViewModel.LoadUserInfoAsync();

        NavigateToPage(_userViewModel);
    }

    [RelayCommand]
    private async Task GetDailyRecommendations()
    {
        await _dailyRecommendViewModel.LoadContentAsync();
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task Search()
    {
        if (string.IsNullOrWhiteSpace(SearchKeyword)) return;

        NavigateToPage(_searchViewModel);

        await _searchViewModel.SearchAsync(SearchKeyword);
    }

    private bool CanSearch()
    {
        return !string.IsNullOrWhiteSpace(SearchKeyword);
    }

    [RelayCommand]
    private async Task OpenSidebarPlaylist(PlaylistItem? item)
    {
        if (item == null || item.Type == PlaylistType.AddButton) return;

        NavigateToPage(PlaylistsViewModel);
        await PlaylistsViewModel.OpenPlaylistCommand.ExecuteAsync(item);
    }

    private void OnPlaylistViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MyPlaylistsViewModel.SelectedPlaylist))
            UpdateSidebarSelection();
    }

    private void OnPlaylistItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshSidebarPlaylists();
    }

    private void UpdateSidebarSelection()
    {
        var selected = PlaylistsViewModel.SelectedPlaylist;

        foreach (var item in SidebarCreatedPlaylists)
            item.IsSelected = IsSameItem(selected, item);
        foreach (var item in SidebarCollectedPlaylists)
            item.IsSelected = IsSameItem(selected, item);
        foreach (var item in SidebarAlbumPlaylists)
            item.IsSelected = IsSameItem(selected, item);
    }

    private static bool IsSameItem(PlaylistItem? a, PlaylistItem b)
    {
        if (a == null) return false;
        if (a.Type != b.Type) return false;

        return a.Type switch
        {
            PlaylistType.Online or PlaylistType.Album => a.ListId > 0 && b.ListId > 0
                ? a.ListId == b.ListId
                : string.Equals(a.Id, b.Id, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private void RefreshSidebarPlaylists()
    {
        SidebarCreatedPlaylists.Clear();
        SidebarCollectedPlaylists.Clear();
        SidebarAlbumPlaylists.Clear();

        SidebarCreatedPlaylists.AddRange(PlaylistsViewModel.Items.AsValueEnumerable().Where(IsCreatedOnlinePlaylist).ToArray());
        SidebarCollectedPlaylists.AddRange(PlaylistsViewModel.Items.AsValueEnumerable().Where(IsCollectedOnlinePlaylist).ToArray());
        SidebarAlbumPlaylists.AddRange(PlaylistsViewModel.Items.AsValueEnumerable().Where(x => x.Type == PlaylistType.Album).ToArray());

        UpdateSidebarSelection();
    }

    private static bool IsCreatedOnlinePlaylist(PlaylistItem item)
    {
        return item.Type == PlaylistType.Online && item.UserPlaylistType == 0;
    }

    private static bool IsCollectedOnlinePlaylist(PlaylistItem item)
    {
        return item.Type == PlaylistType.Online && item.UserPlaylistType != 0;
    }

    [RelayCommand]
    private void ToggleDesktopLyric()
    {
        SettingsManager.Settings.OpenDesktopLyricOnStartup = !_desktopLyricWindowService.IsOpen;
        SettingsManager.Save();
        _desktopLyricWindowService.Toggle();
    }


    [RelayCommand]
    private void NavigateBack()
    {
        if (_navigationService.GoBack())
            return;

        var dailyVm = Pages.AsValueEnumerable().OfType<DailyRecommendViewModel>().FirstOrDefault();
        if (dailyVm != null) _navigationService.NavigateRoot(dailyVm);
    }


    public void ForceCloseDesktopLyric()
    {
        _isClosingDesktopLyricForShutdown = true;
        _desktopLyricWindowService.Close();
    }

    private async Task HandlePlaySongMessageAsync(SongItem song)
    {
        try
        {
            IList<SongItem>? currentSongList = null;

            if (ActivePage is DailyRecommendViewModel dailyVm)
                currentSongList = dailyVm.Songs;
            else if (ActivePage is MyPlaylistsViewModel playlistVm && playlistVm.IsShowingSongs)
                currentSongList = playlistVm.SelectedPlaylistSongs;
            else if (ActivePage is LocalMusicLibraryViewModel localMusicVm && localMusicVm.IsShowingSongs)
                currentSongList = localMusicVm.SelectedPlaylistSongs;
            else if (ActivePage is DiscoverViewModel discoverVm && discoverVm.IsShowingSongs)
                currentSongList = discoverVm.SelectedPlaylistSongs;
            else if (ActivePage is SearchViewModel searchVm)
                currentSongList = searchVm.IsShowingDetail ? searchVm.DetailSongs : searchVm.Songs;
            else if (ActivePage is UserCloudViewModel userCloudVm)
                currentSongList = userCloudVm.Songs;
            else if (ActivePage is SingerViewModel singerVm)
                currentSongList = singerVm.IsAlbumDetailVisible ? singerVm.AlbumSongs : singerVm.Songs;
            else if (ActivePage is RankViewModel rankVm && rankVm.IsShowingSongs)
                currentSongList = rankVm.SelectedRankSongs;
            else if (ActivePage is HistoryViewModel historyVm)
                currentSongList = historyVm.Songs;

            await Player.PlaySongAsync(song, currentSongList);
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellations from rapid song switching.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理播放歌曲消息失败");
        }
    }

}
