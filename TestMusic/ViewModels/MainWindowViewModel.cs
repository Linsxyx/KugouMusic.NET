using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Clients;
using KuGou.Net.Protocol.Session;
using TestMusic.Views;

namespace TestMusic.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private const string DefaultCover = "avares://TestMusic/Assets/Default.png";
    private const string LikeListCover = "avares://TestMusic/Assets/LikeList.jpg";
    private readonly DeviceClient _deviceClient;
    private readonly DiscoveryClient _discoveryClient;

    public PlayerViewModel Player{ get; }
    private readonly PlaylistClient _playlistClient;
    private readonly SearchViewModel _searchViewModel;
    private readonly KgSessionManager _sessionManager;
    private readonly UserClient _userClient;
    private readonly UserViewModel _userViewModel;

    [ObservableProperty] private PageViewModelBase _activePage;
    [ObservableProperty] private LyricLineViewModel? _currentLyricLine;
    
    [ObservableProperty] private bool _isLoggedIn;

    [ObservableProperty] private bool _isNowPlayingOpen;
    [ObservableProperty] private bool _isQueuePaneOpen; // 右侧抽屉

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _searchKeyword = "";

    [ObservableProperty] private string _statusMessage = "就绪";
    [ObservableProperty] private string? _userAvatar;

    [ObservableProperty] private string _userName = "未登录";
    
    public MainWindowViewModel(
        PlayerViewModel player,
        KgSessionManager sessionManager,
        AuthClient authClient,
        DeviceClient deviceClient,
        DiscoveryClient discoveryClient,
        PlaylistClient playlistClient,
        UserClient userClient,
        LoginViewModel loginViewModel,
        SearchViewModel searchViewModel,
        UserViewModel userViewModel)
    {
        _sessionManager = sessionManager;
        _deviceClient = deviceClient;
        _discoveryClient = discoveryClient;
        
        _playlistClient = playlistClient;
        _userClient = userClient;
        
        LoginViewModel = loginViewModel;
        _searchViewModel = searchViewModel;
        _userViewModel = userViewModel;

        
        LoginViewModel.LoginSuccess += OnLoginSuccess;
        _userViewModel.LogoutRequested += OnLogoutRequested;

        Player = player;
        var dailyVm = new DailyRecommendViewModel();
        var playlistVm = new MyPlaylistsViewModel();
        Pages.Add(dailyVm);
        Pages.Add(playlistVm);
        ActivePage = dailyVm;
        
        Player.PropertyChanged += (s, e) => 
        {
            if (e.PropertyName == nameof(Player.StatusMessage))
                StatusMessage = Player.StatusMessage;
        };
        
        Task.Run(async () =>
        {
            await LoadLocalSessionOrLogin();
            await GetDailyRecommendations();
        });
    }
    
    public Window? MainWindow { get; set; }

    public ObservableCollection<PageViewModelBase> Pages { get; } = new();
    
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
                StatusMessage = $"已加载本地用户: {saved.UserId}";
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await TryGetVip();
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = $"获取VIP失败: {ex.Message}";
                    }
                });
            }
            else
            {
                StatusMessage = "未登录，以游客身份运行。";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"登录初始化失败: {ex.Message}";
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
            // 忽略加载用户信息失败
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
            StatusMessage = "登录成功";

            // 后台初始化设备
            _ = Task.Run(async () =>
            {
                try
                {
                    await _deviceClient.InitDeviceAsync();
                }
                catch
                {
                    // 忽略
                }
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
            StatusMessage = "已退出登录";

            // 返回每日推荐页面
            var dailyVm = Pages.OfType<DailyRecommendViewModel>().FirstOrDefault();
            if (dailyVm != null) ActivePage = dailyVm;
        });
    }

    [RelayCommand]//先用弹窗，之后再改
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

        StatusMessage = "正在获取每日推荐...";
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
                        Cover = string.IsNullOrWhiteSpace(item.SizableCover)
                            ? DefaultCover
                            : item.SizableCover,
                        DurationSeconds = item.Duration
                    });
                StatusMessage = $"每日推荐加载完成 ({response.Date})";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"获取推荐失败: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task Search()
    {
        if (string.IsNullOrWhiteSpace(SearchKeyword)) return;
        
        ActivePage = _searchViewModel;
        
        await _searchViewModel.SearchAsync(SearchKeyword);
        StatusMessage = _searchViewModel.StatusMessage;
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
        StatusMessage = "正在获取个人歌单...";
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
            StatusMessage = $"加载了 {vm.Playlists.Count} 个歌单。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"获取歌单失败: {ex.Message}";
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

        StatusMessage = "正在加载歌单歌曲...";
        try
        {
            var songs = await _playlistClient.GetSongsAsync(playlistId, pageSize: 100);
            vm.SelectedPlaylistSongs.Clear();
            foreach (var item in songs)
            {
                // 复原原本的歌手名解析逻辑
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

            StatusMessage = "歌单加载完成。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载歌单失败: {ex.Message}";
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
    private async Task PlayFromList(SongItem? song)
    {
        if (song == null) return;

        IList<SongItem>? currentSongList = null;
        
        if (ActivePage is DailyRecommendViewModel dailyVm)
        {
            currentSongList = dailyVm.Songs;
        }
        else if (ActivePage is MyPlaylistsViewModel playlistVm && playlistVm.IsShowingSongs)
        {
            currentSongList = playlistVm.SelectedPlaylistSongs;
        }
        else if (ActivePage is SearchViewModel searchVm)
        {
            currentSongList = searchVm.Songs;
        }
        
        await Player.PlaySongAsync(song, currentSongList);
    }
}