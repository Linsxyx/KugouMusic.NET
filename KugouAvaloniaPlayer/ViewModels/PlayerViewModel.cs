using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Adapters.Lyrics;
using KuGou.Net.Clients;
using Microsoft.Extensions.Logging;
using SimpleAudio;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class PlayerViewModel : ViewModelBase, IDisposable
{
    // [新增] 固定歌单ID (根据你的描述)
    private const string LikeListIdForAction = "2";
    private readonly ISukiDialogManager _dialogManager;

    // Dictionary 用于删除时，通过Hash找到对应的 FileId (API要求)
    private readonly Dictionary<string, int> _hashToFileId = new();


    private readonly HashSet<string> _likedHashes = new();
    private readonly ILogger<PlayerViewModel> _logger;
    private readonly LyricClient _lyricClient;
    private readonly MusicClient _musicClient;
    private readonly DispatcherTimer _playbackTimer;
    private readonly SimpleAudioPlayer _player;
    private readonly PlaylistClient _playlistClient;
    private readonly Random _random = new();
    private readonly ISukiToastManager _toastManager;

    private readonly UserClient _userClient;

    [ObservableProperty] private LyricLineViewModel? _currentLyricLine;

    private List<KrcLine> _currentLyrics = new();

    [ObservableProperty] private string _currentLyricText = "---";
    [ObservableProperty] private string _currentLyricTrans = "";

    [ObservableProperty] private SongItem? _currentPlayingSong;
    [ObservableProperty] private double _currentPositionSeconds;

    //private Dictionary<string, int> _favoriteCache = new(StringComparer.OrdinalIgnoreCase);
    [ObservableProperty] private bool _isCurrentSongLiked;
    [ObservableProperty] private bool _isDraggingProgress;

    [ObservableProperty] private bool _isLiked;
    [ObservableProperty] private bool _isPlayingAudio;

    [ObservableProperty] private bool _isShuffleMode;

    [ObservableProperty] private bool _isTogglingLike;
    [ObservableProperty] private string _musicQuality = "high";

    [ObservableProperty] private float _musicVolume = 1.0f;

    private List<SongItem> _originalQueue = new();

    //[ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private double _totalDurationSeconds;

    public PlayerViewModel(MusicClient musicClient, LyricClient lyricClient, ISukiToastManager toastManager,
        ISukiDialogManager dialogManager,
        UserClient userClient, PlaylistClient playlistClient, ILogger<PlayerViewModel> logger)
    {
        _musicClient = musicClient;
        _lyricClient = lyricClient;
        _toastManager = toastManager;
        _dialogManager = dialogManager;
        _userClient = userClient;
        _playlistClient = playlistClient;
        _logger = logger;

        _player = new SimpleAudioPlayer();

        _player.PlaybackEnded += OnPlaybackEnded;

        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _playbackTimer.Tick += OnPlaybackTimerTick;
    }

    public AvaloniaList<LyricLineViewModel> LyricLines { get; } = new();

    // 播放队列 (独立于页面)
    public AvaloniaList<SongItem> PlaybackQueue { get; } = new();

    public void Dispose()
    {
        _playbackTimer.Stop();
        _playbackTimer.Tick -= OnPlaybackTimerTick;

        _player.PlaybackEnded -= OnPlaybackEnded;
        _player.Dispose();

        _currentLyrics.Clear();
        LyricLines.Clear();
        _originalQueue.Clear();
        PlaybackQueue.Clear();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     核心播放逻辑
    /// </summary>
    /// <param name="song">要播放的歌曲</param>
    /// <param name="contextList">
    ///     歌曲所在的上下文列表。
    ///     如果不为空，说明用户在列表中点击了播放，需要替换队列。
    ///     如果为空，说明用户在点击"上一首/下一首"，保持队列不变。
    /// </param>
    public async Task PlaySongAsync(SongItem? song, IList<SongItem>? contextList = null)
    {
        if (song == null) return;

        if (contextList != null && contextList.Any())
        {
            const int maxQueueSize = 300;

            _originalQueue.Clear();
            PlaybackQueue.Clear();

            IEnumerable<SongItem> targetList = contextList;

            if (contextList.Count > maxQueueSize)
            {
                var currentIndex = contextList.IndexOf(song);
                if (currentIndex >= 0)
                {
                    var start = Math.Max(0, currentIndex - maxQueueSize / 2);
                    var count = Math.Min(contextList.Count - start, maxQueueSize);
                    targetList = contextList.Skip(start).Take(count);

                    _logger.LogInformation($"列表过大，已截取 {start} 到 {start + count} 范围的歌曲进入队列");
                }
                else
                {
                    targetList = contextList.Take(maxQueueSize);
                }
            }

            var finalList = targetList.ToList();

            _originalQueue.AddRange(finalList);

            if (IsShuffleMode)
            {
                var shuffleList = new List<SongItem>(finalList);
                shuffleList.Remove(song);

                var n = shuffleList.Count;
                while (n > 1)
                {
                    n--;
                    var k = _random.Next(n + 1);
                    (shuffleList[k], shuffleList[n]) = (shuffleList[n], shuffleList[k]);
                }

                var finalQueue = new List<SongItem> { song };
                finalQueue.AddRange(shuffleList);

                PlaybackQueue.AddRange(finalQueue);
            }
            else
            {
                PlaybackQueue.AddRange(finalList);
            }
        }
        else if (PlaybackQueue.Count == 0)
        {
            _originalQueue.Add(song);
            PlaybackQueue.Add(song);
        }

        if (CurrentPlayingSong != null) CurrentPlayingSong.IsPlaying = false;
        CurrentPlayingSong = song;
        CurrentPlayingSong.IsPlaying = true;
        CheckCurrentSongLikeStatus();
        _logger.LogInformation($"正在缓冲: {song.Name}");

        StopAndReset();
        try
        {
            string? url;
            if (File.Exists(song.LocalFilePath))
            {
                url = song.LocalFilePath;
                _logger.LogInformation($"正在播放本地文件: {song.Name}");
            }
            else
            {
                var playData = await _musicClient.GetPlayInfoAsync(song.Hash, MusicQuality);
                if (playData != null && playData.Status != 1)
                {
                    _toastManager.CreateToast()
                        .OfType(NotificationType.Warning)
                        .WithTitle("无法获取播放链接")
                        .WithContent($"错误代码: {playData.ErrCode}, 正在尝试下一首...")
                        .Dismiss().After(TimeSpan.FromSeconds(3))
                        .Queue();

                    await Task.Delay(1000);
                    await PlayNext();
                    return;
                }

                url = playData?.Urls.FirstOrDefault(x => !string.IsNullOrEmpty(x));
                _ = LoadLyrics(song.Hash, song.Name);
            }

            if (url != null && _player.Load(url))
            {
                _player.SetVolume(MusicVolume);
                _player.Play();
                IsPlayingAudio = true;
                TotalDurationSeconds =
                    song.DurationSeconds > 0 ? song.DurationSeconds : _player.GetDuration().TotalSeconds;
                _playbackTimer.Start();
                _logger.LogInformation($"正在播放: {song.Name}");
            }
            else
            {
                _logger.LogError("音频流加载失败");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"播放出错: {ex.Message}");
            StopAndReset();
        }
    }

    [RelayCommand]
    private async Task PlaySong(SongItem? song)
    {
        await PlaySongAsync(song);
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
    private async Task PlayNext()
    {
        if (PlaybackQueue.Count == 0) return;
        var idx = CurrentPlayingSong != null ? PlaybackQueue.IndexOf(CurrentPlayingSong) : -1;
        var nextIdx = (idx + 1) % PlaybackQueue.Count;
        await PlaySongAsync(PlaybackQueue[nextIdx]);
    }

    [RelayCommand]
    private async Task PlayPrevious()
    {
        if (PlaybackQueue.Count == 0) return;
        var idx = CurrentPlayingSong != null ? PlaybackQueue.IndexOf(CurrentPlayingSong) : -1;
        var prevIdx = idx - 1;
        if (prevIdx < 0) prevIdx = PlaybackQueue.Count - 1;
        await PlaySongAsync(PlaybackQueue[prevIdx]);
    }

    [RelayCommand]
    private void ClearQueue()
    {
        PlaybackQueue.Clear();
        _originalQueue.Clear();
        StopAndReset();
    }

    [RelayCommand]
    private void RemoveFromQueue(SongItem song)
    {
        PlaybackQueue.Remove(song);
        _originalQueue.Remove(song);
        if (PlaybackQueue.Count == 0) StopAndReset();
    }

    [RelayCommand]
    private void AddToNext(SongItem? song)
    {
        if (song == null) return;
        var originalIdx = CurrentPlayingSong != null ? _originalQueue.IndexOf(CurrentPlayingSong) : -1;
        if (originalIdx >= 0 && originalIdx < _originalQueue.Count - 1)
            _originalQueue.Insert(originalIdx + 1, song);
        else
            _originalQueue.Add(song);

        var playIdx = CurrentPlayingSong != null ? PlaybackQueue.IndexOf(CurrentPlayingSong) : -1;
        if (playIdx >= 0 && playIdx < PlaybackQueue.Count - 1)
            PlaybackQueue.Insert(playIdx + 1, song);
        else
            PlaybackQueue.Add(song);

        _logger.LogInformation("已添加到播放队列");
    }

    [RelayCommand]
    private void ToggleShuffleMode()
    {
        IsShuffleMode = !IsShuffleMode;

        if (PlaybackQueue.Count == 0) return;

        var currentSong = CurrentPlayingSong;

        if (IsShuffleMode)
        {
            var songsToShuffle = PlaybackQueue.Where(x => x != currentSong).ToList();
            var n = songsToShuffle.Count;
            while (n > 1)
            {
                n--;
                var k = _random.Next(n + 1);
                (songsToShuffle[k], songsToShuffle[n]) = (songsToShuffle[n], songsToShuffle[k]);
            }

            PlaybackQueue.Clear();
            if (currentSong != null) PlaybackQueue.Add(currentSong);
            foreach (var song in songsToShuffle) PlaybackQueue.Add(song);
        }
        else
        {
            if (_originalQueue.Count != PlaybackQueue.Count) _originalQueue = PlaybackQueue.ToList();

            PlaybackQueue.Clear();
            foreach (var song in _originalQueue) PlaybackQueue.Add(song);
        }
    }

    private void StopAndReset()
    {
        _playbackTimer.Stop();
        _player.Stop();
        IsPlayingAudio = false;
        CurrentLyricText = "---";
        CurrentLyricTrans = "";
        CurrentPositionSeconds = 0;
        LyricLines.Clear();
    }

    private void OnPlaybackTimerTick(object? sender, EventArgs e)
    {
        if (_player.IsStopped || IsDraggingProgress) return;
        var pos = _player.GetPosition();
        CurrentPositionSeconds = pos.TotalSeconds;
        UpdateLyrics(pos.TotalMilliseconds);
    }

    // 拖动进度条逻辑
    partial void OnCurrentPositionSecondsChanged(double value)
    {
        if (Math.Abs(value - _player.GetPosition().TotalSeconds) < 0.5) return;
        _player.SetPosition(TimeSpan.FromSeconds(value));
        UpdateLyrics(value * 1000);
    }

    partial void OnMusicVolumeChanged(float value)
    {
        _player.SetVolume(value);
    }

    private async Task LoadLyrics(string hash, string name)
    {
        CurrentLyricTrans = "";
        _currentLyrics.Clear();

        LyricLines.Clear();
        CurrentLyricLine = null;

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

                    foreach (var line in _currentLyrics)
                        LyricLines.Add(new LyricLineViewModel
                        {
                            Content = line.Content,
                            Translation = line.Translation,
                            StartTime = line.StartTime,
                            Duration = line.Duration,
                            IsActive = false
                        });

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
        if (LyricLines.Count == 0) return;

        var left = 0;
        var right = LyricLines.Count - 1;
        var resultIndex = 0;
        if (currentMs < LyricLines[0].StartTime)
            resultIndex = 0;
        else if (currentMs >= LyricLines[^1].StartTime)
            resultIndex = LyricLines.Count - 1;
        else
            while (left <= right)
            {
                var mid = left + (right - left) / 2;

                if (LyricLines[mid].StartTime <= currentMs)
                {
                    resultIndex = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }

        var currentVm = LyricLines[resultIndex];

        if (currentVm != CurrentLyricLine)
        {
            if (CurrentLyricLine != null) CurrentLyricLine.IsActive = false;

            CurrentLyricLine = currentVm;
            CurrentLyricLine.IsActive = true;

            CurrentLyricText = currentVm.Content;
            CurrentLyricTrans = currentVm.Translation;
        }
    }

    public async Task LoadLikeListAsync()
    {
        try
        {
            var playlists = await _userClient.GetPlaylistsAsync();
            if (playlists.Count < 1) return;

            var likePlaylist = playlists[1];
            if (string.IsNullOrEmpty(likePlaylist.ListCreateId))
            {
                _logger.LogError("歌单获取失败");
                return;
            }

            var songs = await _playlistClient.GetSongsAsync(likePlaylist.ListCreateId, pageSize: 1000);
            

            lock (_likedHashes)
            {
                _likedHashes.Clear();
                _hashToFileId.Clear();
                foreach (var song in songs)
                    if (!string.IsNullOrEmpty(song.Hash))
                    {
                        _likedHashes.Add(song.Hash.ToLowerInvariant());
                        if (song.FileId != 0) _hashToFileId[song.Hash.ToLowerInvariant()] = song.FileId;
                    }
            }

            CheckCurrentSongLikeStatus();
        }
        catch (Exception ex)
        {
            _logger.LogError($"加载收藏列表失败: {ex.Message}");
        }
    }

    private void CheckCurrentSongLikeStatus()
    {
        if (CurrentPlayingSong != null && !string.IsNullOrEmpty(CurrentPlayingSong.Hash))
        {
            var hash = CurrentPlayingSong.Hash.ToLowerInvariant();
            IsLiked = _likedHashes.Contains(hash);
        }
        else
        {
            IsLiked = false;
        }
    }

    [RelayCommand]
    private async Task ToggleLike()
    {
        if (CurrentPlayingSong == null) return;
        var song = CurrentPlayingSong;
        var hash = song.Hash.ToLowerInvariant();

        try
        {
            if (IsLiked)
            {
                if (_hashToFileId.TryGetValue(hash, out var fileId))
                {
                    var idList = new List<long> { fileId };
                    var result = await _playlistClient.RemoveSongsAsync(LikeListIdForAction, idList);

                    if (result.Status == 1)
                    {
                        IsLiked = false;
                        _likedHashes.Remove(hash);
                        _hashToFileId.Remove(hash);
                        _logger.LogInformation("已取消收藏");
                    }
                    else
                    {
                        _logger.LogError($"取消失败: {result.ErrorCode}");
                    }
                }
                else
                {
                    _logger.LogInformation("正在同步列表，请稍后再试...");
                    await LoadLikeListAsync();
                }
            }
            else
            {
                var songList = new List<(string Name, string Hash, string AlbumId, string MixSongId)>
                {
                    (song.Name, song.Hash, song.AlbumId ?? "0", "0")
                };

                var result = await _playlistClient.AddSongsAsync(LikeListIdForAction, songList);

                if (result.Status == 1)
                {
                    IsLiked = true;
                    _likedHashes.Add(hash);
                    _logger.LogInformation("已添加到我喜欢");

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1000); // 稍等API生效
                        await LoadLikeListAsync();
                    });
                }
                else
                {
                    _logger.LogError($"收藏失败: {result.ErrorCode}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"操作失败: {ex.Message}");
        }
    }

    private void OnPlaybackEnded()
    {
        Dispatcher.UIThread.Post(() => PlayNextCommand.Execute(null));
    }

    [RelayCommand]
    public async Task ShowAddToPlaylistDialog(SongItem song)
    {
        if (song == null) return;

        try
        {
            // 获取歌单列表
            var playlists = await _userClient.GetPlaylistsAsync();
            var onlinePlaylists = playlists
                .Where(p => !string.IsNullOrEmpty(p.ListCreateId))
                .ToList();

            if (onlinePlaylists.Count == 0)
            {
                _toastManager.CreateToast()
                    .OfType(NotificationType.Warning)
                    .WithTitle("没有可用的歌单")
                    .WithContent("请先创建歌单")
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Dismiss().ByClicking()
                    .Queue();
                return;
            }

            // 创建歌单选择列表
            var listBox = new ListBox
            {
                Width = 300,
                MaxHeight = 400,
                ItemsSource = onlinePlaylists,
                SelectionMode = SelectionMode.Single,
                ItemTemplate = new FuncDataTemplate<UserPlaylistItem>((item, _) =>
                {
                    return new TextBlock
                    {
                        Text = item.Name
                    };
                })
            };

            _dialogManager.CreateDialog()
                .WithTitle("添加到歌单")
                .WithContent(listBox)
                .WithActionButton("取消", _ => { }, true, "Standard")
                .WithActionButton("添加", async _ =>
                {
                    if (listBox.SelectedItem is UserPlaylistItem selectedPlaylist)
                        await AddSongToPlaylistAsync(song, selectedPlaylist.ListId.ToString(), selectedPlaylist.Name);
                }, true, "Standard")
                .TryShow();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取歌单列表失败");
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("获取歌单失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
    }

    private async Task AddSongToPlaylistAsync(SongItem song, string playlistId, string playlistName)
    {
        try
        {
            var songList = new List<(string Name, string Hash, string AlbumId, string MixSongId)>
            {
                (song.Name, song.Hash, song.AlbumId ?? "0", "0")
            };

            var result = await _playlistClient.AddSongsAsync(playlistId, songList);

            if (result.Status == 1)
                _toastManager.CreateToast()
                    .OfType(NotificationType.Success)
                    .WithTitle("添加成功")
                    .WithContent($"已将「{song.Name}」添加到「{playlistName}」")
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Dismiss().ByClicking()
                    .Queue();
            else
                _toastManager.CreateToast()
                    .OfType(NotificationType.Error)
                    .WithTitle("添加失败")
                    .WithContent($"错误代码: {result.ErrorCode}")
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Dismiss().ByClicking()
                    .Queue();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "添加歌曲到歌单失败");
            _toastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("添加失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
    }
}