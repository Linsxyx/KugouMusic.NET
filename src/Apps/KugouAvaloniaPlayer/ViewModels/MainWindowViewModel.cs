using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Collections;
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
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private static readonly IBrush DefaultLyricBrush = new SolidColorBrush(Colors.White);
    private static readonly IBrush DefaultTranslationLineBrush = new SolidColorBrush(Color.Parse("#CCFFFFFF"));
    private static readonly IBrush DefaultTranslationWordBrush = new SolidColorBrush(Colors.White);
    private readonly LoginClient _authClient;
    private readonly IAppUpdateService _appUpdateService;
    private readonly IDesktopLyricWindowService _desktopLyricWindowService;
    private readonly DailyRecommendViewModel _dailyRecommendViewModel;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly ILoginDialogService _loginDialogService;
    private readonly INavigationService _navigationService;
    private readonly SearchViewModel _searchViewModel;
    private readonly KgSessionManager _sessionManager;
    private readonly UserClient _userClient;
    private readonly UserViewModel _userViewModel;

    [ObservableProperty]
    public partial PageViewModelBase ActivePage { get; set; }

    [ObservableProperty]
    public partial LyricLineViewModel? CurrentLyricLine { get; set; }

    [ObservableProperty]
    public partial bool IsDesktopLyricEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsLoggedIn { get; set; }

    [ObservableProperty]
    public partial bool EnableLegacyWordLyricEffect { get; set; }

    [ObservableProperty]
    public partial bool IsNowPlayingOpen { get; set; }

    [ObservableProperty]
    public partial bool IsNowPlayingVolumeVisible { get; set; }

    [ObservableProperty]
    public partial bool IsQueuePaneOpen { get; set; }

    private bool _isUpdatingActivePageFromNavigation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNowPlayingPrimaryLyricVisible))]
    [NotifyPropertyChangedFor(nameof(IsNowPlayingTranslationVisible))]
    [NotifyPropertyChangedFor(nameof(IsNowPlayingRomanizationVisible))]
    public partial NowPlayingLyricDisplayMode NowPlayingLyricDisplayMode { get; set; } = NowPlayingLyricDisplayMode.LyricsWithTranslation;

    [ObservableProperty] 
    public partial FontFamily NowPlayingLyricFontFamily { get; set; } = FontFamily.Default;

    [ObservableProperty]
    public partial double NowPlayingLyricFontSize { get; set; } = 26;

    [ObservableProperty]
    public partial IBrush NowPlayingLyricForeground { get; set; } = DefaultLyricBrush;

    [ObservableProperty]
    public partial HorizontalAlignment NowPlayingLyricHorizontalAlignment { get; set; } = HorizontalAlignment.Center;

    [ObservableProperty]
    public partial TextAlignment NowPlayingLyricTextAlignment { get; set; } = TextAlignment.Center;

    [ObservableProperty]
    public partial double NowPlayingTranslationFontSize { get; set; } = 16;

    [ObservableProperty]
    public partial IBrush NowPlayingTranslationLineForeground { get; set; } = DefaultTranslationLineBrush;

    [ObservableProperty]
    public partial IBrush NowPlayingTranslationWordForeground { get; set; } = DefaultTranslationWordBrush;
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
        LoginViewModel loginViewModel,
        SearchViewModel searchViewModel,
        UserViewModel userViewModel,
        RankViewModel rankViewModel,
        DailyRecommendViewModel dailyRecommendViewModel,
        HistoryViewModel historyViewModel,
        DiscoverViewModel discoverViewModel,
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
        _userViewModel = userViewModel;
        PlaylistsViewModel = myPlaylistsViewModel;
        _logger = logger;

        _userViewModel.CheckForUpdateRequested += OnCheckForUpdateRequested;
        _desktopLyricWindowService.IsOpenChanged += OnDesktopLyricWindowStateChanged;

        Player = player;
        ToastManager = toastManager;

        Pages.Add(_dailyRecommendViewModel);
        Pages.Add(historyViewModel);
        Pages.Add(discoverViewModel);
        Pages.Add(rankViewModel);
        Pages.Add(_searchViewModel);
        _navigationService.CurrentPageChanged += OnNavigationCurrentPageChanged;
        _navigationService.ReplaceRoot(_dailyRecommendViewModel);
        ActivePage = _dailyRecommendViewModel;
        IsDesktopLyricEnabled = _desktopLyricWindowService.IsOpen;
        EnableLegacyWordLyricEffect = SettingsManager.Settings.EnableLegacyWordLyricEffect;
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
            (_, m) => { NavigateToPage(m.TargetPage); });

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
            EnableLegacyWordLyricEffect = message.EnableLegacyWordLyricEffect;
        });

        Task.Run(async () =>
        {
            await LoadLocalSessionOrLogin();
            await GetDailyRecommendations();
            if (SettingsManager.Settings.AutoCheckUpdate) await _appUpdateService.CheckForUpdatesAsync();
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
    public AvaloniaList<PlaylistItem> SidebarAlbumPlaylists { get; } = new();
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

        NavigateToPage(value);
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

    private void NavigateToPage(PageViewModelBase page)
    {
        if (_navigationService.CurrentPage == page)
            return;

        if (Pages.Contains(page))
        {
            _navigationService.ReplaceRoot(page);
            return;
        }

        _navigationService.Push(page);
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
#if DEBUG
                var defaultFontFamily = FontFamily.Default;
                string defaultFontName = defaultFontFamily.Name;
                _logger.LogInformation($"字体为{defaultFontName}");
#endif
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
            Player.ClearPersonalFmSession();
            _ = _dailyRecommendViewModel.OnAuthStateChangedAsync();
            _logger.LogInformation("已退出登录");

            // 返回每日推荐页面
            _navigationService.ReplaceRoot(_dailyRecommendViewModel);
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

    private void OnPlaylistItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshSidebarPlaylists();
    }

    private void RefreshSidebarPlaylists()
    {
        SidebarOnlinePlaylists.Clear();
        SidebarLocalPlaylists.Clear();
        SidebarAlbumPlaylists.Clear();

        SidebarOnlinePlaylists.AddRange(PlaylistsViewModel.Items.Where(x => x.Type == PlaylistType.Online));
        SidebarLocalPlaylists.AddRange(PlaylistsViewModel.Items.Where(x => x.Type == PlaylistType.Local));
        SidebarAlbumPlaylists.AddRange(PlaylistsViewModel.Items.Where(x => x.Type == PlaylistType.Album));
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
    private void NavigateBack()
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
            NowPlayingLyricFontFamily = FontFamily.Default;
            return;
        }

        NowPlayingLyricFontFamily = IsSystemFontInstalled(fontFamilyName)
            ? new FontFamily(fontFamilyName)
            : FontFamily.Default;
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
