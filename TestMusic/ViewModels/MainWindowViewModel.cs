using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KuGou.Audio;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Adapters.Lyrics;
using KuGou.Net.Clients;
using KuGou.Net.Protocol.Session;


namespace TestMusic.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    
    private readonly KgSessionManager _sessionManager;
    private readonly AuthClient _authClient;
    private readonly DeviceClient _deviceClient; 
    private readonly DiscoveryClient _discoveryClient;
    private readonly MusicClient _musicClient;
    private readonly PlaylistClient _playlistClient;
    private readonly UserClient _userClient;
    private readonly LyricClient _lyricClient;

    
    private readonly SimpleAudioPlayer _player;
    private readonly DispatcherTimer _playbackTimer;

    
    public ObservableCollection<SongItem> Songs { get; } = new();
    public ObservableCollection<PlaylistItem> UserPlaylists { get; } = new();
    
    
    private List<KrcLine> _currentLyrics = new();

    
    
    [ObservableProperty] 
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _searchKeyword = "";

    [ObservableProperty] private string _statusMessage = "就绪";
    
    [ObservableProperty] private double _currentPositionSeconds;
    [ObservableProperty] private double _totalDurationSeconds;
    [ObservableProperty] private string _currentLyricText = "";
    [ObservableProperty] private string _currentLyricTrans = "";
    
    [ObservableProperty] private bool _isLoggedIn = false;
    
    
    [ObservableProperty] 
    [NotifyCanExecuteChangedFor(nameof(TogglePlayPauseCommand))]
    private bool _isPlayingAudio; 

    [ObservableProperty] private SongItem? _currentPlayingSong;
    
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

        
        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _playbackTimer.Tick += OnPlaybackTimerTick;
        
        Task.Run(LoadLocalSessionOrLogin);
    }

    
    private void OnPlaybackTimerTick(object? sender, EventArgs e)
    {
        if (_player.IsStopped)
        {
            StopPlayback();
            
            return;
        }

        var pos = _player.GetPosition();
        CurrentPositionSeconds = pos.TotalSeconds;
        
        
        UpdateLyrics(pos.TotalMilliseconds);
    }

    private void UpdateLyrics(double currentMs)
    {
        if (_currentLyrics.Count == 0) return;

        
        KrcLine? currentLine = null;
        
        
        currentLine = _currentLyrics.FirstOrDefault(x => currentMs >= x.StartTime && currentMs < x.StartTime + x.Duration);

        
        if (currentLine == null)
        {
            currentLine = _currentLyrics.LastOrDefault(x => x.StartTime <= currentMs);
        }

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
                DurationSeconds = item.Duration
            }));

            StatusMessage = $"搜索完成，找到 {Songs.Count} 首歌曲。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"搜索失败: {ex.Message}";
        }
    }
    private bool CanSearch() => !string.IsNullOrWhiteSpace(SearchKeyword);

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
            {
                if (!string.IsNullOrEmpty(item.GlobalId))
                {
                    UserPlaylists.Add(new PlaylistItem
                    {
                        Name = item.Name,
                        Id = item.GlobalId,
                        Count = item.Count
                    });
                }
            }
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

        // UI状态重置
        if (CurrentPlayingSong != null) CurrentPlayingSong.IsPlaying = false;
        CurrentPlayingSong = song;
        CurrentPlayingSong.IsPlaying = true;
        
        StatusMessage = $"正在请求播放: {song.Name}";
        
        try
        {
            // 1. 获取播放地址
            var playData = await _musicClient.GetPlayInfoAsync(song.Hash, "high");
            if (playData?.Status != 1 || playData.Urls.Count == 0)
            {
                StatusMessage = "无法获取播放链接。";
                StopPlayback();
                return;
            }

            var url = playData.Urls.FirstOrDefault(x => !string.IsNullOrEmpty(x));
            if (string.IsNullOrEmpty(url)) 
            {
                StatusMessage = "播放链接无效。";
                return;
            }

            // 2. 获取歌词
            await LoadLyrics(song.Hash, song.Name);

            // 3. 播放
            if (_player.Load(url))
            {
                _player.Play();
                _player.SetVolume(1.0f);
                IsPlayingAudio = true;
                TotalDurationSeconds = song.DurationSeconds > 0 ? song.DurationSeconds : _player.GetDuration().TotalSeconds;
                _playbackTimer.Start();
                StatusMessage = $"正在播放: {song.Name}";
            }
            else
            {
                StatusMessage = "播放器加载音频失败。";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"播放出错: {ex.Message}";
            StopPlayback();
        }
    }

    [RelayCommand(CanExecute = nameof(IsPlayingAudio))] // 简单用这个属性控制
    private void TogglePlayPause()
    {
        if (_player.IsStopped) return;
        
        
        StopPlayback();
        StatusMessage = "已停止播放。";
    }

    private void StopPlayback()
    {
        _playbackTimer.Stop();
        _player.Stop();
        IsPlayingAudio = false;
        CurrentLyricText = "---";
        CurrentLyricTrans = "";
        CurrentPositionSeconds = 0;
        if(CurrentPlayingSong != null) CurrentPlayingSong.IsPlaying = false;
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
        CurrentLyricText = "正在获取歌词...";
        CurrentLyricTrans = "";
        _currentLyrics.Clear();

        try
        {
            var searchJson = await _lyricClient.SearchLyricAsync(hash, null, name, "no");
            
            
            if (!searchJson.TryGetProperty("candidates", out var candidatesElem) || candidatesElem.ValueKind != JsonValueKind.Array) return;
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
                        CurrentLyricText = "歌词加载成功";
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