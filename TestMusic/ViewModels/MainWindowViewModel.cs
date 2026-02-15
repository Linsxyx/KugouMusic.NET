using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Adapters.Lyrics;
using KuGou.Net.Clients;
using KuGou.Net.Protocol.Session;
using SimpleAudio;

namespace TestMusic.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private const string DefaultCover = "avares://TestMusic/Assets/Default.png";
    private const string LikeListCover = "avares://TestMusic/Assets/LikeList.jpg";
    private readonly AuthClient _authClient;
    private readonly DeviceClient _deviceClient;
    private readonly DiscoveryClient _discoveryClient;
    private readonly LyricClient _lyricClient;
    private readonly MusicClient _musicClient;
    private readonly DispatcherTimer _playbackTimer;

    private readonly SimpleAudioPlayer _player;
    private readonly PlaylistClient _playlistClient;
    private readonly KgSessionManager _sessionManager;
    private readonly UserClient _userClient;

    [ObservableProperty] private PageViewModelBase _activePage;

    private List<KrcLine> _currentLyrics = new();

    // --- 歌词与显示 ---
    [ObservableProperty] private string _currentLyricText = "---";
    [ObservableProperty] private string _currentLyricTrans = "";

    [ObservableProperty] private SongItem? _currentPlayingSong;
    [ObservableProperty] private double _currentPositionSeconds;
    [ObservableProperty] private bool _isDraggingProgress;
    [ObservableProperty] private bool _isLoggedIn;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(TogglePlayPauseCommand))]
    private bool _isPlayingAudio;

    [ObservableProperty] private bool _isQueuePaneOpen; // 右侧抽屉
    private bool _isTimerUpdatingPosition;
    [ObservableProperty] private string _musicQuality = "high";
    [ObservableProperty] private float _musicVolume = 1.0f;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _searchKeyword = "";

    [ObservableProperty] private string _statusMessage = "就绪";
    [ObservableProperty] private double _totalDurationSeconds;


    // --- 构造函数 ---
    public MainWindowViewModel(
        KgSessionManager sessionManager,
        AuthClient authClient,
        DeviceClient deviceClient,
        DiscoveryClient discoveryClient,
        MusicClient musicClient,
        PlaylistClient playlistClient,
        UserClient userClient,
        LyricClient lyricClient)
    {
        _sessionManager = sessionManager;
        _authClient = authClient;
        _deviceClient = deviceClient;
        _discoveryClient = discoveryClient;
        _musicClient = musicClient;
        _playlistClient = playlistClient;
        _userClient = userClient;
        _lyricClient = lyricClient;

        _player = new SimpleAudioPlayer();
        _player.PlaybackEnded += () => Dispatcher.UIThread.Post(() => PlayNextCommand.Execute(null));

        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _playbackTimer.Tick += OnPlaybackTimerTick;

        var dailyVm = new DailyRecommendViewModel();
        var playlistVm = new MyPlaylistsViewModel();
        Pages.Add(dailyVm);
        Pages.Add(playlistVm);
        ActivePage = dailyVm;

        // 启动后台任务：登录 & 加载推荐
        Task.Run(async () =>
        {
            await LoadLocalSessionOrLogin();
            await GetDailyRecommendations();
        });
    }

    public ObservableCollection<PageViewModelBase> Pages { get; } = new();
    public ObservableCollection<SongItem> PlaybackQueue { get; } = new();

    // --- 原版登录与VIP逻辑 (完全复原) ---
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


    [RelayCommand]
    private async Task PlaySong(SongItem? song)
    {
        if (song == null) return;

        // 获取当前显示的页面
        var currentPage = ActivePage;

        // 根据当前页面类型获取歌曲列表
        List<SongItem> songsToQueue = new();

        if (currentPage is DailyRecommendViewModel dailyVm)
            songsToQueue = dailyVm.Songs.ToList();
        else if (currentPage is MyPlaylistsViewModel playlistVm && playlistVm.IsShowingSongs)
            songsToQueue = playlistVm.SelectedPlaylistSongs.ToList();

        // 如果当前页面没有歌曲列表，或者列表为空，只添加当前歌曲
        if (songsToQueue.Count == 0) songsToQueue = new List<SongItem> { song };

        // 清空并重新添加队列
        PlaybackQueue.Clear();
        foreach (var item in songsToQueue) PlaybackQueue.Add(item);

        // UI 更新
        if (_currentPlayingSong != null) _currentPlayingSong.IsPlaying = false;
        CurrentPlayingSong = song;
        CurrentPlayingSong.IsPlaying = true;

        StatusMessage = $"正在缓冲: {song.Name}";
        StopAndReset(); // 停止上一首

        try
        {
            // 1. 获取播放链接
            var playData = await _musicClient.GetPlayInfoAsync(song.Hash, MusicQuality);
            if (playData?.Status != 1 || playData.Urls.Count == 0)
            {
                StatusMessage = "无法获取播放链接，尝试下一首...";
                await Task.Delay(1000);
                await PlayNext();
                return;
            }

            var url = playData.Urls.FirstOrDefault(x => !string.IsNullOrEmpty(x));
            if (string.IsNullOrEmpty(url)) return;

            _ = LoadLyrics(song.Hash, song.Name);

            // 3. 加载音频并播放
            if (_player.Load(url))
            {
                _player.SetVolume(MusicVolume);
                _player.Play();

                IsPlayingAudio = true;
                // 优先使用 API 返回的时长，否则尝试从播放器获取
                TotalDurationSeconds =
                    song.DurationSeconds > 0 ? song.DurationSeconds : _player.GetDuration().TotalSeconds;

                _playbackTimer.Start();
                StatusMessage = $"正在播放: {song.Name}";
            }
            else
            {
                StatusMessage = "加载音频流失败";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"播放出错: {ex.Message}";
            StopAndReset();
        }
    }

    [RelayCommand]
    private void RemoveFromQueue(SongItem song)
    {
        PlaybackQueue.Remove(song);
        if (PlaybackQueue.Count == 0) StopAndReset();
    }

    [RelayCommand]
    private void ClearQueue()
    {
        PlaybackQueue.Clear();
        StopAndReset();
    }


    private void StopAndReset()
    {
        _playbackTimer.Stop();
        _player.Stop();
        IsPlayingAudio = false;
        CurrentLyricText = "---";
        CurrentLyricTrans = "";
        CurrentPositionSeconds = 0;
    }


    private async Task LoadLyrics(string hash, string name)
    {
        CurrentLyricTrans = "";
        _currentLyrics.Clear();

        try
        {
            var searchJson = await _lyricClient.SearchLyricAsync(hash, null, name, "no");

            if (!searchJson.TryGetProperty("candidates", out var candidatesElem) ||
                candidatesElem.ValueKind != JsonValueKind.Array) return;

            var candidates = candidatesElem.EnumerateArray().ToList();
            if (candidates.Count == 0)
            {
                CurrentLyricText = "未找到歌词";
                return;
            }

            var bestMatch = candidates.First();
            var id = bestMatch.GetProperty("id").GetString();
            var key = bestMatch.GetProperty("accesskey").GetString();
            var fmt = bestMatch.TryGetProperty("fmt", out var f) ? f.GetString() ?? "krc" : "krc";

            if (id != null && key != null)
            {
                var lyricResult = await _lyricClient.GetLyricAsync(id, key, fmt);
                if (!string.IsNullOrEmpty(lyricResult.DecodedContent))
                {
                    var krc = KrcParser.Parse(lyricResult.DecodedContent);
                    _currentLyrics = krc.Lines;
                    CurrentLyricText = _currentLyrics.Count > 0 ? "" : "暂无歌词";
                }
            }
        }
        catch
        {
            CurrentLyricText = "歌词获取失败";
        }
    }

    private void UpdateLyrics(double currentMs)
    {
        if (_currentLyrics.Count == 0) return;

        var currentLine =
            _currentLyrics.FirstOrDefault(x => currentMs >= x.StartTime && currentMs < x.StartTime + x.Duration);

        if (currentLine == null) currentLine = _currentLyrics.LastOrDefault(x => x.StartTime <= currentMs);

        if (currentLine != null)
        {
            CurrentLyricText = currentLine.Content;
            CurrentLyricTrans = currentLine.Translation;
        }
    }

    private void OnPlaybackTimerTick(object? sender, EventArgs e)
    {
        if (_player.IsStopped) return;

        if (IsDraggingProgress) return;

        var pos = _player.GetPosition();

        CurrentPositionSeconds = pos.TotalSeconds;

        UpdateLyrics(pos.TotalMilliseconds);
    }

    partial void OnCurrentPositionSecondsChanged(double value)
    {
        if (_isTimerUpdatingPosition) return;

        if (Math.Abs(value - _player.GetPosition().TotalSeconds) < 0.5) return;


        _player.SetPosition(TimeSpan.FromSeconds(value));

        UpdateLyrics(value * 1000);
    }

    partial void OnMusicVolumeChanged(float value)
    {
        _player.SetVolume(value);
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

        // 暂时借用 DailyRecommendVM 来显示搜索结果，或者你可以新建一个 SearchViewModel
        var vm = Pages.OfType<DailyRecommendViewModel>().FirstOrDefault();
        if (vm == null) return;

        // 切换到该页面
        ActivePage = vm;
        StatusMessage = $"正在搜索: {SearchKeyword} ...";

        try
        {
            var response = await _musicClient.SearchAsync(SearchKeyword);
            vm.Songs.Clear(); // 清空原有列表

            foreach (var item in response)
                vm.Songs.Add(new SongItem
                {
                    Name = item.Name,
                    Singer = item.Singer,
                    Hash = item.Hash,
                    Cover =
                        string.IsNullOrWhiteSpace(item.Cover) ? DefaultCover : item.Cover,
                    DurationSeconds = item.Duration
                });
            StatusMessage = $"搜索完成，找到 {vm.Songs.Count} 首歌曲。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"搜索失败: {ex.Message}";
        }
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
        vm.IsShowingSongs = true; // 切换到详情模式

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
    private async Task PlayNext()
    {
        if (PlaybackQueue.Count == 0) return;
        var idx = CurrentPlayingSong != null ? PlaybackQueue.IndexOf(CurrentPlayingSong) : -1;
        var nextIdx = (idx + 1) % PlaybackQueue.Count;
        await PlaySong(PlaybackQueue[nextIdx]);
    }

    [RelayCommand]
    private async Task PlayPrevious()
    {
        if (PlaybackQueue.Count == 0) return;
        var idx = CurrentPlayingSong != null ? PlaybackQueue.IndexOf(CurrentPlayingSong) : -1;
        var prevIdx = idx - 1;
        if (prevIdx < 0) prevIdx = PlaybackQueue.Count - 1;
        await PlaySong(PlaybackQueue[prevIdx]);
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (_player.IsPlaying)
        {
            _player.Pause();
            IsPlayingAudio = false;
            _playbackTimer.Stop();
        }
        else
        {
            _player.Play();
            IsPlayingAudio = true;
            _playbackTimer.Start();
        }
    }

    [RelayCommand]
    private void AddToNext(SongItem? song)
    {
        if (song == null) return;
        var idx = CurrentPlayingSong != null ? PlaybackQueue.IndexOf(CurrentPlayingSong) : -1;
        if (idx >= 0 && idx < PlaybackQueue.Count - 1) PlaybackQueue.Insert(idx + 1, song);
        else PlaybackQueue.Add(song);
        StatusMessage = "已添加到播放队列";
    }

    [RelayCommand]
    private void ToggleQueuePane()
    {
        IsQueuePaneOpen = !IsQueuePaneOpen;
    }
}