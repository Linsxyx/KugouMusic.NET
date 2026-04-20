using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Layout;
using Avalonia.Media;
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
    private static readonly IBrush DefaultLyricBrush = new SolidColorBrush(Colors.White);
    private static readonly IBrush DefaultTranslationLineBrush = new SolidColorBrush(Color.Parse("#CCFFFFFF"));
    private static readonly IBrush DefaultTranslationWordBrush = new SolidColorBrush(Colors.White);
    private readonly AuthClient _authClient;
    private readonly IDesktopLyricWindowService _desktopLyricWindowService;
    private readonly DiscoveryClient _discoveryClient;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly ILoginDialogService _loginDialogService;
    private readonly INavigationService _navigationService;
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
    private bool _isUpdatingActivePageFromNavigation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNowPlayingPrimaryLyricVisible))]
    [NotifyPropertyChangedFor(nameof(IsNowPlayingTranslationVisible))]
    [NotifyPropertyChangedFor(nameof(IsNowPlayingRomanizationVisible))]
    private NowPlayingLyricDisplayMode _nowPlayingLyricDisplayMode = NowPlayingLyricDisplayMode.LyricsWithTranslation;

    [ObservableProperty] private FontFamily? _nowPlayingLyricFontFamily;
    [ObservableProperty] private double _nowPlayingLyricFontSize = 26;
    [ObservableProperty] private IBrush _nowPlayingLyricForeground = DefaultLyricBrush;
    [ObservableProperty] private HorizontalAlignment _nowPlayingLyricHorizontalAlignment = HorizontalAlignment.Center;
    [ObservableProperty] private TextAlignment _nowPlayingLyricTextAlignment = TextAlignment.Center;
    [ObservableProperty] private double _nowPlayingTranslationFontSize = 16;
    [ObservableProperty] private IBrush _nowPlayingTranslationLineForeground = DefaultTranslationLineBrush;
    [ObservableProperty] private IBrush _nowPlayingTranslationWordForeground = DefaultTranslationWordBrush;

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
        INavigationService navigationService,
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
        _navigationService = navigationService;

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
        Pages.Add(_searchViewModel);
        _navigationService.CurrentPageChanged += OnNavigationCurrentPageChanged;
        _navigationService.ReplaceRoot(dailyRecommendViewModel);
        ActivePage = dailyRecommendViewModel;
        IsDesktopLyricEnabled = _desktopLyricWindowService.IsOpen;
        ApplyNowPlayingLyricStyleSettings(
            SettingsManager.Settings.PlayPageLyricUseCustomMainColor,
            SettingsManager.Settings.PlayPageLyricCustomMainColor,
            SettingsManager.Settings.PlayPageLyricUseCustomTranslationColor,
            SettingsManager.Settings.PlayPageLyricCustomTranslationColor,
            SettingsManager.Settings.PlayPageLyricUseCustomFont,
            SettingsManager.Settings.PlayPageLyricCustomFontFamily,
            SettingsManager.Settings.PlayPageLyricAlignment,
            SettingsManager.Settings.PlayPageLyricFontSize);
        NowPlayingLyricDisplayMode = SettingsManager.Settings.PlayPageLyricDisplayMode;

        PlaylistsViewModel.Items.CollectionChanged += OnPlaylistItemsChanged;
        RefreshSidebarPlaylists();

        WeakReferenceMessenger.Default.Register<PlaySongMessage>(this,
            (_, m) => _ = HandlePlaySongMessageAsync(m.Song));

        WeakReferenceMessenger.Default.Register<NavigateToSingerMessage>(this, (_, m) =>
        {
            var singerVm = singerViewModelFactory1.Create(m.Singer.Id.ToString(), m.Singer.Name);
            _navigationService.Push(singerVm);
        });

        WeakReferenceMessenger.Default.Register<AuthStateChangedMessage>(this, (_, m) =>
        {
            if (m.IsLoggedIn)
                _ = OnLoginSuccessAsync();
            else
                OnLogoutRequested();
        });

        WeakReferenceMessenger.Default.Register<NavigatePageMessage>(this,
            (_, m) => { _navigationService.Push(m.TargetPage); });

        WeakReferenceMessenger.Default.Register<RequestNavigateBackMessage>(this, (_, _) => { NavigateBack(); });
        WeakReferenceMessenger.Default.Register<LyricStyleSettingsChangedMessage>(this, (_, message) =>
        {
            if (message.Scope != LyricSettingsScope.PlayPage)
                return;

            ApplyNowPlayingLyricStyleSettings(
                message.UseCustomMainColor,
                message.MainColorHex,
                message.UseCustomTranslationColor,
                message.TranslationColorHex,
                message.UseCustomFont,
                message.FontFamilyName,
                message.Alignment,
                message.FontSize);
        });

        Task.Run(async () =>
        {
            await LoadLocalSessionOrLogin();
            await GetDailyRecommendations();
            if (SettingsManager.Settings.AutoCheckUpdate) await CheckForUpdatesAsync();
        });
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
    public bool IsNowPlayingPrimaryLyricVisible => true;

    public bool IsNowPlayingTranslationVisible =>
        NowPlayingLyricDisplayMode == NowPlayingLyricDisplayMode.LyricsWithTranslation;

    public bool IsNowPlayingRomanizationVisible =>
        NowPlayingLyricDisplayMode == NowPlayingLyricDisplayMode.LyricsWithRomanization;

    private void OnDesktopLyricWindowStateChanged(bool isOpen)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            IsDesktopLyricEnabled = isOpen;
            return;
        }

        Dispatcher.UIThread.Post(() => IsDesktopLyricEnabled = isOpen);
    }

    partial void OnActivePageChanged(PageViewModelBase value)
    {
        if (_isUpdatingActivePageFromNavigation)
            return;

        if (_navigationService.CurrentPage != value)
            _navigationService.Push(value);
    }

    private void OnNavigationCurrentPageChanged(PageViewModelBase? page)
    {
        if (page == null)
            return;

        if (Dispatcher.UIThread.CheckAccess())
        {
            _isUpdatingActivePageFromNavigation = true;
            ActivePage = page;
            _isUpdatingActivePageFromNavigation = false;
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            _isUpdatingActivePageFromNavigation = true;
            ActivePage = page;
            _isUpdatingActivePageFromNavigation = false;
        });
    }


    /*public PlaylistItem SidebarAddPlaylistItem { get; } = new()
    {
        Name = "新建/添加",
        Type = PlaylistType.AddButton
    };*/

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
                _logger.LogInformation($"已加载本地用户: {session.UserId}");
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
                _userViewModel.UserId = _sessionManager.Session.UserId;
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
            _userViewModel.UserName = UserName;
            _userViewModel.UserAvatar = null;
            _userViewModel.UserId = string.Empty;
            _userViewModel.VipStatus = "未开通";
            _logger.LogInformation("已退出登录");

            // 返回每日推荐页面
            var dailyVm = Pages.OfType<DailyRecommendViewModel>().FirstOrDefault();
            if (dailyVm != null) _navigationService.ReplaceRoot(dailyVm);
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
        _navigationService.Push(_userViewModel);
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

        _navigationService.Push(_searchViewModel);

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

        _navigationService.Push(PlaylistsViewModel);
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
    private void ToggleNowPlayingLyricDisplayMode()
    {
        NowPlayingLyricDisplayMode = NowPlayingLyricDisplayMode switch
        {
            NowPlayingLyricDisplayMode.LyricsWithTranslation => NowPlayingLyricDisplayMode.LyricsOnly,
            NowPlayingLyricDisplayMode.LyricsOnly => NowPlayingLyricDisplayMode.LyricsWithRomanization,
            _ => NowPlayingLyricDisplayMode.LyricsWithTranslation
        };
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
        if (_navigationService.TryGoBack())
            return;

        var dailyVm = Pages.OfType<DailyRecommendViewModel>().FirstOrDefault();
        if (dailyVm != null) _navigationService.ReplaceRoot(dailyVm);
    }


    public void ForceCloseDesktopLyric()
    {
        _desktopLyricWindowService.Close();
    }

    private void ApplyNowPlayingLyricStyleSettings(
        bool useCustomMainColor,
        string mainColorHex,
        bool useCustomTranslationColor,
        string translationColorHex,
        bool useCustomFont,
        string fontFamilyName,
        LyricAlignmentOption alignment,
        double fontSize)
    {
        ApplyNowPlayingFontSettings(useCustomFont, fontFamilyName);
        ApplyNowPlayingAlignmentSettings(alignment);
        ApplyNowPlayingFontSizeSettings(fontSize);

        NowPlayingLyricForeground = useCustomMainColor
            ? new SolidColorBrush(ParseColorOrDefault(mainColorHex, Colors.White))
            : DefaultLyricBrush;

        if (useCustomTranslationColor)
        {
            var color = new SolidColorBrush(ParseColorOrDefault(translationColorHex, Color.Parse("#CCFFFFFF")));
            NowPlayingTranslationLineForeground = color;
            NowPlayingTranslationWordForeground = color;
            return;
        }

        NowPlayingTranslationLineForeground = DefaultTranslationLineBrush;
        NowPlayingTranslationWordForeground = DefaultTranslationWordBrush;
    }

    private void ApplyNowPlayingFontSettings(bool useCustomFont, string fontFamilyName)
    {
        if (!useCustomFont || string.IsNullOrWhiteSpace(fontFamilyName))
        {
            NowPlayingLyricFontFamily = null;
            return;
        }

        NowPlayingLyricFontFamily = IsSystemFontInstalled(fontFamilyName)
            ? new FontFamily(fontFamilyName)
            : null;
    }

    private void ApplyNowPlayingAlignmentSettings(LyricAlignmentOption alignment)
    {
        switch (alignment)
        {
            case LyricAlignmentOption.Left:
                NowPlayingLyricHorizontalAlignment = HorizontalAlignment.Left;
                NowPlayingLyricTextAlignment = TextAlignment.Left;
                break;
            case LyricAlignmentOption.Right:
                NowPlayingLyricHorizontalAlignment = HorizontalAlignment.Right;
                NowPlayingLyricTextAlignment = TextAlignment.Right;
                break;
            default:
                NowPlayingLyricHorizontalAlignment = HorizontalAlignment.Center;
                NowPlayingLyricTextAlignment = TextAlignment.Center;
                break;
        }
    }

    private void ApplyNowPlayingFontSizeSettings(double fontSize)
    {
        var clamped = Math.Clamp(fontSize, 18, 42);
        NowPlayingLyricFontSize = clamped;
        NowPlayingTranslationFontSize = Math.Max(14, Math.Round(clamped * 0.62, 1));
    }

    private static bool IsSystemFontInstalled(string fontFamilyName)
    {
        foreach (var systemFont in FontManager.Current.SystemFonts)
            if (string.Equals(systemFont.Name, fontFamilyName, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    private static Color ParseColorOrDefault(string? colorText, Color fallback)
    {
        return Color.TryParse(colorText, out var parsed) ? parsed : fallback;
    }

    partial void OnNowPlayingLyricDisplayModeChanged(NowPlayingLyricDisplayMode value)
    {
        SettingsManager.Settings.PlayPageLyricDisplayMode = value;
        SettingsManager.Save();
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
        ISukiToast? toast = null;

        var hideButton = new Button
        {
            Content = "x",
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        ToolTip.SetTip(hideButton, "后台继续下载");

        hideButton.Click += (_, _) =>
        {
            if (toast is not null)
                ToastManager.Dismiss(toast);
        };

        var progressContent = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowSpacing = 8
        };
        progressContent.Children.Add(new TextBlock
        {
            Text = "下载会在后台继续进行。",
            Opacity = 0.72,
            VerticalAlignment = VerticalAlignment.Center
        });
        progressContent.Children.Add(hideButton);
        Grid.SetColumn(hideButton, 1);
        progressContent.Children.Add(progress);
        Grid.SetRow(progress, 1);
        Grid.SetColumnSpan(progress, 2);

        toast = ToastManager.CreateToast()
            .WithTitle("正在下载更新...")
            .WithContent(progressContent)
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