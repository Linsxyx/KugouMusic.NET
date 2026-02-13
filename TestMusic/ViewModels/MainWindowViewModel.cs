using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimpleAudio;
using KuGou.Net.Adapters.Lyrics;
using KuGou.Net.Clients;
using KuGou.Net.Protocol.Session;

namespace TestMusic.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
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
    
    
    private bool _isTimerUpdatingPosition; 
    
    private List<KrcLine> _currentLyrics = new();
    
    public ObservableCollection<SongItem> Songs { get; } = new();
    public ObservableCollection<PlaylistItem> UserPlaylists { get; } = new();
    
    
    [ObservableProperty] 
    private string _currentLyricText = "";
    [ObservableProperty] 
    private string _currentLyricTrans = "";

    [ObservableProperty] 
    private SongItem? _currentPlayingSong;

    [ObservableProperty] 
    private double _currentPositionSeconds;

    [ObservableProperty]
    private bool _isLoggedIn;
    
    [ObservableProperty]
    private bool _isDraggingProgress;


    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TogglePlayPauseCommand))]
    private bool _isPlayingAudio;
    
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _searchKeyword = "";

    [ObservableProperty]
    private string _statusMessage = "就绪";
    
    [ObservableProperty] 
    private double _totalDurationSeconds ;
    
    public string FormattedTime => FormatTime(TotalDurationSeconds);

    private string FormatTime(double seconds)
    {
        if (seconds < 0)
            return "0m 0s";

        int minutes = (int)(seconds / 60);
        int remainingSeconds = (int)(seconds % 60);
        return $"{minutes}m {remainingSeconds}s";
    }

    [ObservableProperty]
    private string _musicQuality = "high";
    
    [ObservableProperty] 
    private float _musicVolume = 1.0f;
    

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
        
        _player.PlaybackEnded += () => Dispatcher.UIThread.Post(PlayNext);


        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _playbackTimer.Tick += OnPlaybackTimerTick;

        Task.Run(LoadLocalSessionOrLogin);
    }
    
    
    partial void OnCurrentPositionSecondsChanged(double value)
    {
        // 如果是 Timer 更新的，不执行 Seek
        if (_isTimerUpdatingPosition) return;

        // 如果差异太小（比如浮点误差），忽略
        if (Math.Abs(value - _player.GetPosition().TotalSeconds) < 0.5) return;

        // 执行 Seek
        _player.SetPosition(TimeSpan.FromSeconds(value));
        
        // 如果拖动时歌词也要跟手，可以在这里调用 UpdateLyrics(value * 1000)
        UpdateLyrics(value * 1000);
    }

    // 当 UI 改变音量时触发
    partial void OnMusicVolumeChanged(float value)
    {
        _player.SetVolume(value);
    }

    // --- 定时器逻辑 ---
    private void OnPlaybackTimerTick(object? sender, EventArgs e)
    {
        if (_player.IsStopped) return;
        
        if (IsDraggingProgress) return;

        var pos = _player.GetPosition();
    
        // 只有在非拖拽时才更新 VM 属性
        CurrentPositionSeconds = pos.TotalSeconds;
    
        UpdateLyrics(pos.TotalMilliseconds);
    }
    
    public void SeekTo(TimeSpan time)
    {
        _player.SetPosition(time);
    
        // 立即更新一下 UI，防止跳变
        CurrentPositionSeconds = time.TotalSeconds;
        UpdateLyrics(time.TotalMilliseconds);
    }

    // --- 播放控制命令 ---

    [RelayCommand]
    private void PlayNext()
    {
        if (Songs.Count == 0) return;

        var currentIndex = -1;
        if (CurrentPlayingSong != null)
        {
            currentIndex = Songs.IndexOf(CurrentPlayingSong);
        }

        // 循环播放逻辑：如果是最后一首，回到第一首；否则下一首
        var nextIndex = (currentIndex + 1) % Songs.Count;
        PlaySong(Songs[nextIndex]);
    }

    [RelayCommand]
    private void PlayPrevious()
    {
        if (Songs.Count == 0) return;

        var currentIndex = -1;
        if (CurrentPlayingSong != null)
        {
            currentIndex = Songs.IndexOf(CurrentPlayingSong);
        }

        // 上一首
        var prevIndex = currentIndex - 1;
        if (prevIndex < 0) prevIndex = Songs.Count - 1;
        
        PlaySong(Songs[prevIndex]);
    }

    [RelayCommand(CanExecute = nameof(IsPlayingAudio))] 
    private void TogglePlayPause()
    {
        if (_player.IsPlaying)
        {
            _player.Pause();
            IsPlayingAudio = false; // 切换图标为“播放”
            _playbackTimer.Stop();  // 暂停计时器
            StatusMessage = "已暂停";
        }
        else if (_player.IsPaused)
        {
            _player.Play();
            IsPlayingAudio = true;  // 切换图标为“暂停”
            _playbackTimer.Start();
            StatusMessage = "继续播放";
        }
        else if (_player.IsStopped && CurrentPlayingSong != null)
        {
            // 如果是停止状态但有选中歌，尝试重新播放
            PlaySong(CurrentPlayingSong);
        }
    }

    // 原有的 StopPlayback 改名为 cleanup 性质的，仅在切换歌曲或出错时调用
    private void StopAndReset()
    {
        _playbackTimer.Stop();
        _player.Stop();
        IsPlayingAudio = false;
        CurrentLyricText = "---";
        CurrentLyricTrans = "";
        CurrentPositionSeconds = 0;
    }


    

    private void UpdateLyrics(double currentMs)
    {
        if (_currentLyrics.Count == 0) return;


        KrcLine? currentLine = null;


        currentLine =
            _currentLyrics.FirstOrDefault(x => currentMs >= x.StartTime && currentMs < x.StartTime + x.Duration);


        if (currentLine == null) currentLine = _currentLyrics.LastOrDefault(x => x.StartTime <= currentMs);

        if (currentLine != null)
        {
            CurrentLyricText = currentLine.Content;
            CurrentLyricTrans = currentLine.Translation;
        }
    }


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


            await GetDailyRecommendations();
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
            if (todayRecord is { VipType: "tvip" })
            {
                await _userClient.UpgradeVipRewardAsync();
            }
        }
    }


    [RelayCommand]
    private async Task GetDailyRecommendations()
    {
        StatusMessage = "正在获取每日推荐...";
        try
        {
            var response = await _discoveryClient.GetRecommendedSongsAsync();
            if (response == null || response.Songs.Count == 0)
            {
                StatusMessage = "未获取到推荐歌曲。";
                return;
            }

            UpdateSongList(response.Songs.Select(item => new SongItem
            {
                Name = item.Name,
                Singer = item.SingerName,
                Hash = item.Hash,
                AlbumId = item.AlbumId,
                Cover = string.IsNullOrWhiteSpace(item.SizableCover)
                    ? "avares://TestMusic/Assets/Default.png"
                    : item.SizableCover,
                DurationSeconds = item.Duration
            }));

            StatusMessage = $"每日推荐加载完成 ({response.Date})";
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

        StatusMessage = $"正在搜索: {SearchKeyword} ...";
        try
        {
            var response = await _musicClient.SearchAsync(SearchKeyword);

            UpdateSongList(response.Select(item => new SongItem
            {
                Name = item.Name,
                Singer = item.Singer,
                Hash = item.Hash,
                Cover = string.IsNullOrWhiteSpace(item.Cover)
                    ? "avares://TestMusic/Assets/Default.png"
                    : item.Cover,
                DurationSeconds = item.Duration
            }));

            StatusMessage = $"搜索完成，找到 {Songs.Count} 首歌曲。";
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
    private async Task GetMyPlaylists()
    {
        StatusMessage = "正在获取个人歌单...";
        try
        {
            var playlists = await _userClient.GetPlaylistsAsync();
            if (playlists == null)
            {
                StatusMessage = "获取歌单失败。";
                return;
            }

            UserPlaylists.Clear();
            foreach (var item in playlists)
                if (!string.IsNullOrEmpty(item.GlobalId))
                    UserPlaylists.Add(new PlaylistItem
                    {
                        Name = item.Name,
                        Id = item.GlobalId,
                        Count = item.Count
                    });

            StatusMessage = $"加载了 {UserPlaylists.Count} 个歌单。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"获取歌单失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadPlaylistSongs(string playlistId)
    {
        StatusMessage = "正在加载歌单歌曲...";
        try
        {
            var songs = await _playlistClient.GetSongsAsync(playlistId, pageSize: 60);
            if (songs == null) return;

            UpdateSongList(songs.Select(item =>
            {
                var singerName = "未知";
                if (item.Singers != null && item.Singers.Count > 0)
                    singerName = string.Join("、", item.Singers.Select(s => s.Name));
                else if (item.Name.Contains("-"))
                    singerName = item.Name.Split('-')[0].Trim();

                return new SongItem
                {
                    Name = item.Name,
                    Singer = singerName,
                    Hash = item.Hash,
                    AlbumId = item.AlbumId,
                    Cover = string.IsNullOrWhiteSpace(item.Cover)
                        ? "avares://TestMusic/Assets/Default.png"
                        : item.Cover,
                    DurationSeconds = item.DurationMs / 1000.0
                };
            }));

            StatusMessage = "歌单加载完成。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载歌单失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task PlaySong(SongItem? song)
    {
        if (song == null) return;

        // UI状态更新
        if (CurrentPlayingSong != null) CurrentPlayingSong.IsPlaying = false;
        CurrentPlayingSong = song;
        CurrentPlayingSong.IsPlaying = true;

        StatusMessage = $"正在缓冲: {song.Name}";
        StopAndReset(); // 先停止上一首

        try
        {
            var playData = await _musicClient.GetPlayInfoAsync(song.Hash, MusicQuality);
            if (playData?.Status != 1 || playData.Urls.Count == 0)
            {
                StatusMessage = "无法获取播放链接，尝试下一首...";
                await Task.Delay(1000);
                PlayNext(); // 自动跳过
                return;
            }

            var url = playData.Urls.FirstOrDefault(x => !string.IsNullOrEmpty(x));
            if (string.IsNullOrEmpty(url)) return;

            // 异步加载歌词，不阻塞播放开始
            _ = LoadLyrics(song.Hash, song.Name);

            if (_player.Load(url))
            {
                _player.SetVolume(MusicVolume);
                _player.Play();
                
                IsPlayingAudio = true;
                // 获取时长，优先用 API 返回的，没有则问 Player
                TotalDurationSeconds = song.DurationSeconds > 0 ? song.DurationSeconds : _player.GetDuration().TotalSeconds;
                
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

    private void StopPlayback()
    {
        _playbackTimer.Stop();
        _player.Stop();
        IsPlayingAudio = false;
        CurrentLyricText = "---";
        CurrentLyricTrans = "";
        CurrentPositionSeconds = 0;
        if (CurrentPlayingSong != null) CurrentPlayingSong.IsPlaying = false;
    }

    // --- 辅助方法 ---

    private void UpdateSongList(IEnumerable<SongItem> newSongs)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Songs.Clear();
            foreach (var s in newSongs) Songs.Add(s);
        });
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
            if (candidates.Count == 0) return;
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
                    if (krc != null)
                    {
                        _currentLyrics = krc.Lines;
                        //CurrentLyricText = "歌词加载成功";
                    }
                }
            }
        }
        catch
        {
            CurrentLyricText = "歌词获取失败";
        }
    }
}