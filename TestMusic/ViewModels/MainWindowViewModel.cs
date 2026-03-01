using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using KuGou.Net.Protocol.Session;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;
using TestMusic.Views;

namespace TestMusic.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private const string DefaultCover = "avares://TestMusic/Assets/Default.png";
    private const string LikeListCover = "avares://TestMusic/Assets/LikeList.jpg";
    private readonly DeviceClient _deviceClient;
    private readonly DiscoveryClient _discoveryClient;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly MusicClient _musicClient;
    private readonly PlaylistClient _playlistClient;
    private readonly SearchViewModel _searchViewModel;
    private readonly KgSessionManager _sessionManager;
    private readonly UserClient _userClient;
    private readonly UserViewModel _userViewModel;

    [ObservableProperty] private PageViewModelBase _activePage;
    [ObservableProperty] private LyricLineViewModel? _currentLyricLine;

    [ObservableProperty] private bool _isDesktopLyricEnabled;

    [ObservableProperty] private bool _isLoggedIn;

    [ObservableProperty] private bool _isNowPlayingOpen;
    [ObservableProperty] private bool _isQueuePaneOpen; // 右侧抽屉

    private DesktopLyricWindow? _lyricWindow;

    private PageViewModelBase? _previousPage;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _searchKeyword = "";

    //[ObservableProperty] private string _statusMessage = "就绪";
    [ObservableProperty] private string? _userAvatar;

    [ObservableProperty] private string _userName = "未登录";

    public MainWindowViewModel(
        ISukiToastManager toastManager,
        PlayerViewModel player,
        KgSessionManager sessionManager,
        AuthClient authClient,
        DeviceClient deviceClient,
        DiscoveryClient discoveryClient,
        PlaylistClient playlistClient,
        UserClient userClient,
        MusicClient musicClient,
        LoginViewModel loginViewModel,
        SearchViewModel searchViewModel,
        UserViewModel userViewModel,
        ILogger<MainWindowViewModel> logger)
    {
        _sessionManager = sessionManager;
        _deviceClient = deviceClient;
        _discoveryClient = discoveryClient;

        _playlistClient = playlistClient;
        _userClient = userClient;

        LoginViewModel = loginViewModel;
        _searchViewModel = searchViewModel;
        _userViewModel = userViewModel;
        _logger = logger;
        _musicClient = musicClient;

        LoginViewModel.LoginSuccess += OnLoginSuccess;
        _userViewModel.LogoutRequested += OnLogoutRequested;

        Player = player;
        ToastManager = toastManager;
        var dailyVm = new DailyRecommendViewModel();
        var playlistVm = new MyPlaylistsViewModel(_userClient, _playlistClient);
        Pages.Add(dailyVm);
        Pages.Add(playlistVm);
        ActivePage = dailyVm;

        /*Player.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(Player.StatusMessage))
                _logger.LogInformation(Player.StatusMessage);
        };*/

        Task.Run(async () =>
        {
            await LoadLocalSessionOrLogin();
            await GetDailyRecommendations();
        });
    }

    public PlayerViewModel Player { get; }
    public ISukiToastManager ToastManager { get; }

    public Window? MainWindow { get; set; }

    public AvaloniaList<PageViewModelBase> Pages { get; } = new();

    public LoginViewModel LoginViewModel { get; }

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
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"登录初始化失败: {ex.Message}");
            ;
        }
    }

    private async Task LoadUserInfo()
    {
        try
        {
            var userInfo = await _userClient.GetUserInfoAsync();
            if (userInfo != null)
            {
                UserName = userInfo.Name ?? "未知用户";
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
                await _userClient.ReceiveOneDayVipAsync();
                await Task.Delay(1000);
                await _userClient.UpgradeVipRewardAsync();
            }

            if (todayRecord is { VipType: "tvip" }) await _userClient.UpgradeVipRewardAsync();
        }
    }

    private void OnLoginSuccess()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            IsLoggedIn = true;
            await LoadUserInfo();
            _logger.LogInformation("登录成功");

            // 后台初始化设备
            _ = Task.Run(async () =>
            {
                if (!await _deviceClient.InitDeviceAsync())
                    ToastManager.CreateToast()
                        .OfType(NotificationType.Error)
                        .WithTitle("获取Dfid失败")
                        .Dismiss().After(TimeSpan.FromSeconds(3))
                        .Dismiss().ByClicking()
                        .Queue();
            });

            // 加载推荐
            await GetDailyRecommendations();
        });
    }

    private void OnLogoutRequested()
    {
        Dispatcher.UIThread.Post(async () =>
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

    [RelayCommand] //先用弹窗，之后再改
    private void ShowLoginDialog()
    {
        if (MainWindow == null) return;

        var loginWindow = new LoginWindow
        {
            DataContext = LoginViewModel
        };

        void OnLoginSuccess()
        {
            loginWindow.Close();
            _ = Task.Run(async () =>
            {
                try
                {
                    await TryGetVip();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"获取VIP失败: {ex.Message}");
                }
            });
        }

        LoginViewModel.LoginSuccess += OnLoginSuccess;
        loginWindow.Closed += (_, _) => LoginViewModel.LoginSuccess -= OnLoginSuccess;

        loginWindow.Show(MainWindow);
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
                foreach (var item in response.Songs)
                    vm.Songs.Add(new SongItem
                    {
                        Name = item.Name,
                        Singer = item.SingerName,
                        Hash = item.Hash,
                        AlbumId = item.AlbumId,
                        Singers = item.Singers,
                        Cover = string.IsNullOrWhiteSpace(item.SizableCover)
                            ? DefaultCover
                            : item.SizableCover,
                        DurationSeconds = item.Duration
                    });
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
    private async Task NavigateToMyPlaylists()
    {
        var vm = Pages.OfType<MyPlaylistsViewModel>().FirstOrDefault();
        if (vm == null) return;

        ActivePage = vm;
        vm.IsShowingSongs = false;

        if (vm.Playlists.Count == 0) await GetMyPlaylists(vm);
    }

    private async Task GetMyPlaylists(MyPlaylistsViewModel vm)
    {
        try
        {
            var playlists = await _userClient.GetPlaylistsAsync();
            vm.Playlists.Clear();
            foreach (var item in playlists)
                if (!string.IsNullOrEmpty(item.GlobalId))
                    vm.Playlists.Add(new PlaylistItem
                    {
                        Name = item.Name,
                        Id = item.GlobalId,
                        Count = item.Count,
                        Cover = string.IsNullOrWhiteSpace(item.Pic)
                            ? item.IsDefault is 2 ? LikeListCover : DefaultCover
                            : item.Pic
                    });
        }
        catch (Exception ex)
        {
            ToastManager.CreateToast()
                .OfType(NotificationType.Warning)
                .WithTitle("获取歌单失败")
                .WithContent($"{ex.Message}")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
    }

    [RelayCommand]
    private async Task LoadPlaylistDetails(string playlistId)
    {
        var vm = Pages.OfType<MyPlaylistsViewModel>().FirstOrDefault();
        if (vm == null) return;

        ActivePage = vm;
        vm.SelectedPlaylist = vm.Playlists.FirstOrDefault(x => x.Id == playlistId);
        vm.IsShowingSongs = true;

        try
        {
            var songs = await _playlistClient.GetSongsAsync(playlistId, pageSize: 100);
            vm.SelectedPlaylistSongs.Clear();
            foreach (var item in songs)
            {
                var singerName = "未知";
                if (item.Singers.Count > 0)
                    singerName = string.Join("、", item.Singers.Select(s => s.Name));

                vm.SelectedPlaylistSongs.Add(new SongItem
                {
                    Name = item.Name,
                    Singer = singerName,
                    Hash = item.Hash,
                    AlbumId = item.AlbumId,
                    Cover =
                        string.IsNullOrWhiteSpace(item.Cover) ? DefaultCover : item.Cover,
                    DurationSeconds = item.DurationMs / 1000.0
                });
            }
        }
        catch (Exception ex)
        {
            ToastManager.CreateToast()
                .OfType(NotificationType.Warning)
                .WithTitle("加载歌单失败")
                .WithContent($"{ex.Message}")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
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
    }
    
    [RelayCommand]
    private void CloseQueuePane()
    {
        IsQueuePaneOpen = false;
    }


    [RelayCommand]
    private async Task PlayFromList(SongItem? song)
    {
        if (song == null) return;

        IList<SongItem>? currentSongList = null;

        if (ActivePage is DailyRecommendViewModel dailyVm)
            currentSongList = dailyVm.Songs;
        else if (ActivePage is MyPlaylistsViewModel playlistVm && playlistVm.IsShowingSongs)
            currentSongList = playlistVm.SelectedPlaylistSongs;
        else if (ActivePage is SearchViewModel searchVm)
        {
            // 如果在搜索详情页（歌单/专辑），使用详情列表
            currentSongList = searchVm.IsShowingDetail ? searchVm.DetailSongs : searchVm.Songs;
        }
        else if (ActivePage is SingerViewModel singerVm) currentSongList = singerVm.Songs;

        await Player.PlaySongAsync(song, currentSongList);
    }


    [RelayCommand]
    private void ToggleDesktopLyric()
    {
        if (_lyricWindow == null)
        {
            _lyricWindow = new DesktopLyricWindow
            {
                DataContext = new DesktopLyricViewModel(Player)
            };

            _lyricWindow.Closed += (s, e) =>
            {
                _lyricWindow = null;
                IsDesktopLyricEnabled = false;
            };

            _lyricWindow.Show();
            IsDesktopLyricEnabled = true;
        }
        else
        {
            _lyricWindow.Close();
            _lyricWindow = null;
            IsDesktopLyricEnabled = false;
        }
    }


    [RelayCommand]
    public void NavigateToSinger(object? parameter)
    {
        if (parameter is SingerLite singer)
        {
            // 记录上一页
            _previousPage = ActivePage;

            // 创建新的 SingerViewModel
            var singerVm = new SingerViewModel(
                _musicClient,
                singer.Id.ToString(),
                singer.Name
            );

            ActivePage = singerVm;
        }
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
            // 默认回首页
            var dailyVm = Pages.OfType<DailyRecommendViewModel>().FirstOrDefault();
            if (dailyVm != null) ActivePage = dailyVm;
        }
    }


    public void ForceCloseDesktopLyric()
    {
        if (_lyricWindow != null)
        {
            _lyricWindow.Close();
            _lyricWindow = null;
            IsDesktopLyricEnabled = false;
        }
    }
}