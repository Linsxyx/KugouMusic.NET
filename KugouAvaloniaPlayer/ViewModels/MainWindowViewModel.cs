using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
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
using SukiUI.Enums;
using SukiUI.Toasts;
using Velopack;
using Velopack.Sources;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private const string DefaultCover = "avares://KugouAvaloniaPlayer/Assets/Default.png";
    private readonly AuthClient _authClient;
    private readonly IDesktopLyricWindowService _desktopLyricWindowService;
    private readonly DiscoveryClient _discoveryClient;
    private readonly ILoginDialogService _loginDialogService;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly SearchViewModel _searchViewModel;
    private readonly KgSessionManager _sessionManager;
    private readonly UserClient _userClient;
    private readonly UserViewModel _userViewModel;

    [ObservableProperty] private PageViewModelBase _activePage;
    [ObservableProperty] private LyricLineViewModel? _currentLyricLine;

    [ObservableProperty] private bool _isDesktopLyricEnabled;

    [ObservableProperty] private bool _isLoggedIn;

    [ObservableProperty] private bool _isNowPlayingOpen;
    [ObservableProperty] private bool _isNowPlayingVolumeVisible;
    [ObservableProperty] private bool _isQueuePaneOpen;

    private PageViewModelBase? _previousPage;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _searchKeyword = "";

    [ObservableProperty] private string? _userAvatar;

    [ObservableProperty] private string _userName = "未登录";

    public MainWindowViewModel(
        ISukiToastManager toastManager,
        PlayerViewModel player,
        ISukiDialogManager dialogManager,
        KgSessionManager sessionManager,
        AuthClient authClient,
        DiscoveryClient discoveryClient,
        UserClient userClient,
        ISingerViewModelFactory singerViewModelFactory,
        IDesktopLyricWindowService desktopLyricWindowService,
        ILoginDialogService loginDialogService,
        LoginViewModel loginViewModel,
        SearchViewModel searchViewModel,
        UserViewModel userViewModel,
        RankViewModel rankViewModel,
        DailyRecommendViewModel dailyRecommendViewModel,
        DiscoverViewModel discoverViewModel,
        MyPlaylistsViewModel myPlaylistsViewModel,
        ILogger<MainWindowViewModel> logger)
    {
        DialogManager = dialogManager;
        _sessionManager = sessionManager;
        _authClient = authClient;
        _discoveryClient = discoveryClient;

        _userClient = userClient;
        var singerViewModelFactory1 = singerViewModelFactory;
        _desktopLyricWindowService = desktopLyricWindowService;
        _loginDialogService = loginDialogService;

        LoginViewModel = loginViewModel;
        _searchViewModel = searchViewModel;
        _userViewModel = userViewModel;
        PlaylistsViewModel = myPlaylistsViewModel;
        _logger = logger;

        _userViewModel.CheckForUpdateRequested += OnCheckForUpdateRequested;
        _desktopLyricWindowService.IsOpenChanged += OnDesktopLyricWindowStateChanged;

        Player = player;
        ToastManager = toastManager;

        Pages.Add(dailyRecommendViewModel);
        Pages.Add(discoverViewModel);
        Pages.Add(rankViewModel);
        ActivePage = dailyRecommendViewModel;
        IsDesktopLyricEnabled = _desktopLyricWindowService.IsOpen;

        PlaylistsViewModel.Items.CollectionChanged += OnPlaylistItemsChanged;
        RefreshSidebarPlaylists();

        WeakReferenceMessenger.Default.Register<PlaySongMessage>(this,
            (_, m) => _ = HandlePlaySongMessageAsync(m.Song));

        WeakReferenceMessenger.Default.Register<NavigateToSingerMessage>(this, (_, m) =>
        {
            _previousPage = ActivePage;
            var singerVm = singerViewModelFactory1.Create(m.Singer.Id.ToString(), m.Singer.Name);
            ActivePage = singerVm;
        });

        WeakReferenceMessenger.Default.Register<AuthStateChangedMessage>(this, (_, m) =>
        {
            if (m.IsLoggedIn)
                _ = OnLoginSuccessAsync();
            else
                OnLogoutRequested();
        });

        WeakReferenceMessenger.Default.Register<NavigatePageMessage>(this, (_, m) =>
        {
            _previousPage = ActivePage;
            ActivePage = m.TargetPage;
        });

        Task.Run(async () =>
        {
            await LoadLocalSessionOrLogin();
            await GetDailyRecommendations();
            if (SettingsManager.Settings.AutoCheckUpdate) await CheckForUpdatesAsync();
        });
    }

    private void OnDesktopLyricWindowStateChanged(bool isOpen)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            IsDesktopLyricEnabled = isOpen;
            return;
        }

        Dispatcher.UIThread.Post(() => IsDesktopLyricEnabled = isOpen);
    }

    public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public PlayerViewModel Player { get; }
    public ISukiToastManager ToastManager { get; }
    public ISukiDialogManager DialogManager { get; }

    public AvaloniaList<PageViewModelBase> Pages { get; } = new();

    private LoginViewModel LoginViewModel { get; }
    public MyPlaylistsViewModel PlaylistsViewModel { get; }

    public AvaloniaList<PlaylistItem> SidebarOnlinePlaylists { get; } = new();
    public AvaloniaList<PlaylistItem> SidebarLocalPlaylists { get; } = new();

    public PlaylistItem SidebarAddPlaylistItem { get; } = new()
    {
        Name = "新建/添加",
        Type = PlaylistType.AddButton
    };

    // --- 登录相关 ---
    private async Task LoadLocalSessionOrLogin()
    {
        try
        {
            var saved = KgSessionStore.Load();
            if (saved != null && !string.IsNullOrEmpty(saved.Token))
            {
                if (!string.IsNullOrEmpty(saved.Dfid))
                {
                    _sessionManager.Session.Dfid = saved.Dfid;
                    _sessionManager.Session.Mid = saved.Mid;
                    _sessionManager.Session.Uuid = saved.Uuid;
                }

                IsLoggedIn = true;
                await LoadUserInfo();
                _logger.LogInformation($"已加载本地用户: {saved.UserId}");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await TryGetVip();
                        await Player.LoadLikeListAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogInformation($"获取VIP失败: {ex.Message}");
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
            _logger.LogError($"登录初始化失败: {ex.Message}");
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
            }
        }
        catch
        {
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
            var todayRecord = history.Items.FirstOrDefault(x => x.Day == todayStr);
            if (todayRecord == null)
            {
                var data = await _userClient.ReceiveOneDayVipAsync();
                if (data is not null && data.Status == 1)
                    _logger.LogInformation("vip领取成功");
                else
                    _logger.LogError($"vip领取失败{data?.ErrorCode}");
                await Task.Delay(1000);
                await _userClient.UpgradeVipRewardAsync();
            }
            else if (todayRecord is { VipType: "tvip" })
            {
                await _userClient.UpgradeVipRewardAsync();
            }
            else
            {
                _logger.LogInformation("今日已领取vip");
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

        _ = Task.Run(async () =>
        {
            try
            {
                await TryGetVip();
                await Player.LoadLikeListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"初始化VIP或喜欢列表失败: {ex.Message}");
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
            _logger.LogInformation("已退出登录");

            // 返回每日推荐页面
            var dailyVm = Pages.OfType<DailyRecommendViewModel>().FirstOrDefault();
            if (dailyVm != null) ActivePage = dailyVm;
        });
    }

    private void OnCheckForUpdateRequested()
    {
        _ = Task.Run(() => CheckForUpdatesAsync(true))
            .ContinueWith(_ => Dispatcher.UIThread.Post(() => _userViewModel.SetCheckingUpdateState(false)));
    }

    [RelayCommand]
    private void ShowLoginDialog()
    {
        _loginDialogService.ShowLoginDialog(LoginViewModel);
    }

    [RelayCommand]
    private void NavigateToUser()
    {
        if (!IsLoggedIn)
        {
            ShowLoginDialog();
            return;
        }

        _ = _userViewModel.LoadUserInfoAsync();
        ActivePage = _userViewModel;
    }

    [RelayCommand]
    private async Task GetDailyRecommendations()
    {
        var vm = Pages.OfType<DailyRecommendViewModel>().FirstOrDefault();
        if (vm == null) return;

        _logger.LogInformation("正在获取每日推荐...");
        try
        {
            var response = await _discoveryClient.GetRecommendedSongsAsync();
            if (response?.Songs != null)
            {
                vm.Songs.Clear();
                var songItems = response.Songs
                    .Select(item => new SongItem
                    {
                        Name = item.Name,
                        Singer = item.SingerName,
                        Hash = item.Hash,
                        AlbumId = item.AlbumId,
                        Singers = item.Singers,
                        Cover = string.IsNullOrWhiteSpace(item.SizableCover) ? DefaultCover : item.SizableCover,
                        DurationSeconds = item.Duration
                    })
                    .ToList();

                if (songItems.Any())
                    vm.Songs.AddRange(songItems);
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation($"获取推荐失败: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task Search()
    {
        if (string.IsNullOrWhiteSpace(SearchKeyword)) return;

        ActivePage = _searchViewModel;

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

        _previousPage = ActivePage;
        ActivePage = PlaylistsViewModel;
        await PlaylistsViewModel.OpenPlaylistCommand.ExecuteAsync(item);
    }

    private void OnPlaylistItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshSidebarPlaylists();
    }

    private void RefreshSidebarPlaylists()
    {
        SidebarOnlinePlaylists.Clear();
        SidebarLocalPlaylists.Clear();

        SidebarOnlinePlaylists.AddRange(PlaylistsViewModel.Items.Where(x => x.Type == PlaylistType.Online));
        SidebarLocalPlaylists.AddRange(PlaylistsViewModel.Items.Where(x => x.Type == PlaylistType.Local));
    }

    [RelayCommand]
    private void ToggleQueuePane()
    {
        IsQueuePaneOpen = !IsQueuePaneOpen;
    }

    [RelayCommand]
    private void OpenNowPlaying()
    {
        IsNowPlayingOpen = true;
    }

    [RelayCommand]
    private void CloseNowPlaying()
    {
        IsNowPlayingOpen = false;
        IsNowPlayingVolumeVisible = false;
    }

    [RelayCommand]
    private void ToggleNowPlayingVolume()
    {
        IsNowPlayingVolumeVisible = !IsNowPlayingVolumeVisible;
    }

    [RelayCommand]
    private void CloseQueuePane()
    {
        IsQueuePaneOpen = false;
    }

    [RelayCommand]
    private void ToggleDesktopLyric()
    {
        _desktopLyricWindowService.Toggle();
    }


    [RelayCommand]
    public void NavigateBack()
    {
        if (_previousPage != null)
        {
            ActivePage = _previousPage;
            _previousPage = null;
        }
        else
        {
            var dailyVm = Pages.OfType<DailyRecommendViewModel>().FirstOrDefault();
            if (dailyVm != null) ActivePage = dailyVm;
        }
    }


    public void ForceCloseDesktopLyric()
    {
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
            else if (ActivePage is DiscoverViewModel discoverVm && discoverVm.IsShowingSongs)
                currentSongList = discoverVm.SelectedPlaylistSongs;
            else if (ActivePage is SearchViewModel searchVm)
                currentSongList = searchVm.IsShowingDetail ? searchVm.DetailSongs : searchVm.Songs;
            else if (ActivePage is SingerViewModel singerVm)
                currentSongList = singerVm.Songs;
            else if (ActivePage is RankViewModel rankVm && rankVm.IsShowingSongs)
                currentSongList = rankVm.SelectedRankSongs;

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


    private async Task CheckForUpdatesAsync(bool showNoUpdateToast = false)
    {
        try
        {
            var repoUrl = "https://github.com/Linsxyx/KugouMusic.NET";
            var updateManager = new UpdateManager(new GithubSource(repoUrl, null, false));


            if (!updateManager.IsInstalled)
            {
                _logger.LogInformation("未通过 Velopack 安装，跳过更新检查。");
                if (showNoUpdateToast)
                    Dispatcher.UIThread.Post(() =>
                    {
                        ToastManager.CreateToast()
                            .OfType(NotificationType.Information)
                            .WithTitle("检查更新")
                            .WithContent("应用未通过安装包安装，无法自动更新。")
                            .Dismiss().After(TimeSpan.FromSeconds(3))
                            .Queue();
                    });
                return;
            }

            var newVersion = await updateManager.CheckForUpdatesAsync();
            if (newVersion == null)
            {
                _logger.LogInformation("当前已是最新版本。");
                if (showNoUpdateToast)
                    Dispatcher.UIThread.Post(() =>
                    {
                        ToastManager.CreateToast()
                            .OfType(NotificationType.Success)
                            .WithTitle("检查更新")
                            .WithContent("当前已是最新版本。")
                            .Dismiss().After(TimeSpan.FromSeconds(3))
                            .Queue();
                    });
                return;
            }

            Dispatcher.UIThread.Post(() => ShowActionToast(updateManager, newVersion));
        }
        catch (Exception ex)
        {
            _logger.LogError($"检查更新失败: {ex.Message}");
            if (showNoUpdateToast)
                Dispatcher.UIThread.Post(() =>
                {
                    ToastManager.CreateToast()
                        .OfType(NotificationType.Error)
                        .WithTitle("检查更新失败")
                        .WithContent(ex.Message)
                        .Dismiss().After(TimeSpan.FromSeconds(4))
                        .Queue();
                });
        }
    }

// 弹出发现更新的 Toast
    private void ShowActionToast(UpdateManager updateManager, UpdateInfo newVersion)
    {
        ToastManager.CreateToast()
            .WithTitle("发现新版本")
            .WithContent($"版本 {newVersion.TargetFullRelease.Version} 现已发布，是否立即更新？")
            .WithActionButton("稍后", _ => { }, true, SukiButtonStyles.Standard)
            .WithActionButton("立即更新", toast => { _ = ShowUpdatingToastAndDownloadAsync(updateManager, newVersion); },
                true, SukiButtonStyles.Standard)
            .Queue();
    }

    private async Task ShowUpdatingToastAndDownloadAsync(UpdateManager updateManager, UpdateInfo newVersion)
    {
        var progress = new ProgressBar { Value = 0, ShowProgressText = true, Minimum = 0, Maximum = 100 };

        var toast = ToastManager.CreateToast()
            .WithTitle("正在下载更新...")
            .WithContent(progress)
            .Queue();

        try
        {
            await Task.Run(async () =>
            {
                await updateManager.DownloadUpdatesAsync(newVersion,
                    percentage => { Dispatcher.UIThread.Post(() => { progress.Value = percentage; }); });
            });

            Dispatcher.UIThread.Post(() =>
            {
                ToastManager.Dismiss(toast);
                ShowReadyToRestartToast(updateManager, newVersion);
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                ToastManager.Dismiss(toast);
                ToastManager.CreateToast()
                    .OfType(NotificationType.Error)
                    .WithTitle("更新下载失败")
                    .WithContent(ex.Message)
                    .Dismiss().After(TimeSpan.FromSeconds(4))
                    .Queue();
            });
        }
    }

    private void ShowReadyToRestartToast(UpdateManager updateManager, UpdateInfo newVersion)
    {
        ToastManager.CreateToast()
            .OfType(NotificationType.Success)
            .WithTitle("更新就绪")
            .WithContent("新版本已下载完毕，重启软件即可应用更新。")
            .WithActionButton("稍后", _ => { }, true, SukiButtonStyles.Standard)
            .WithActionButton("立即重启", _ => { updateManager.ApplyUpdatesAndRestart(newVersion); }, true,
                SukiButtonStyles.Standard)
            .Queue();
    }
}
