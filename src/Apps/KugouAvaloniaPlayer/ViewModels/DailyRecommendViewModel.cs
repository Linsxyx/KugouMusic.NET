using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Clients;
using KuGou.Net.Protocol.Session;
using KugouAvaloniaPlayer.Models;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class DailyRecommendViewModel : PageViewModelBase
{
    private const string DefaultCover = "avares://KugouAvaloniaPlayer/Assets/Default.png";
    private readonly RecommendClient _discoveryClient;
    private readonly ILogger<DailyRecommendViewModel> _logger;
    private readonly PlayerViewModel _player;
    private readonly KgSessionManager _sessionManager;
    private readonly ISukiToastManager _toastManager;
    private CancellationTokenSource? _fmPreviewLoadCancellation;
    private int _fmPreviewRequestVersion;

    [ObservableProperty] private SongItem? _currentFmPreviewSong;
    [ObservableProperty] private bool _isDailyRecommendationsLoading;
    [ObservableProperty] private bool _isFmActive;
    [ObservableProperty] private bool _isFmLoading;
    [ObservableProperty] private PersonalFmMode _selectedFmMode = PersonalFmMode.Peak;
    [ObservableProperty] private PersonalFmSongPoolId _selectedFmSongPoolId = PersonalFmSongPoolId.Taste;

    public DailyRecommendViewModel(
        RecommendClient discoveryClient,
        PlayerViewModel player,
        KgSessionManager sessionManager,
        ISukiToastManager toastManager,
        ILogger<DailyRecommendViewModel> logger)
    {
        _discoveryClient = discoveryClient;
        _player = player;
        _sessionManager = sessionManager;
        _toastManager = toastManager;
        _logger = logger;

        _player.PersonalFmStateChanged += SyncFmStateFromPlayer;
        _player.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PlayerViewModel.IsPlayingAudio) or nameof(PlayerViewModel.CurrentPlayingSong))
            {
                OnPropertyChanged(nameof(IsFmPlaying));
                OnPropertyChanged(nameof(FmPlayIconPath));
            }
        };
    }

    public override string DisplayName => "每日推荐";
    public override string Icon => "/Assets/Radio.svg";
    public string DailyRecommendSubtitle => "今天为你精选的好音乐";

    public AvaloniaList<SongItem> Songs { get; } = new();
    public AvaloniaList<SongItem> PersonalFmSongs { get; } = new();
    public PlayerViewModel Player => _player;

    public PersonalFmSongPoolOption[] FmSongPoolOptions { get; } =
    [
        new(PersonalFmSongPoolId.Taste, "根据口味"),
        new(PersonalFmSongPoolId.Style, "根据风格")
    ];

    public bool CanUsePersonalFm => !string.IsNullOrWhiteSpace(_sessionManager.Session.Token) &&
                                    _sessionManager.Session.UserId != "0";

    public bool HasPersonalFmSongs => PersonalFmSongs.Count > 0;
    public bool ShowFmLoginState => !CanUsePersonalFm && !IsFmLoading;
    public bool ShowFmEmptyState => CanUsePersonalFm && !IsFmLoading && !HasPersonalFmSongs;
    public bool ShowFmSongs => CanUsePersonalFm && !IsFmLoading && HasPersonalFmSongs;
    public bool CanEditFmConfiguration => !IsFmLoading;
    public bool ShowFmControls => CanUsePersonalFm && !IsFmLoading && HasPersonalFmSongs;
    public bool ShowCompactFmPreview => ShowFmSongs && PersonalFmSongs.Count > 1;
    public bool IsFmPlaying => IsFmActive && _player.IsPlayingAudio;
    public string FmCardTitle => PersonalFmPresentation.GetTitle(SelectedFmMode);
    public string FmModeShortLabel => PersonalFmPresentation.GetModeLabel(SelectedFmMode);
    public string FmSongPoolButtonText => SelectedFmSongPoolId == PersonalFmSongPoolId.Style ? "风格" : "口味";
    public string FmCardTagline => $"{PersonalFmPresentation.GetModeLabel(SelectedFmMode)} · {PersonalFmPresentation.GetSongPoolLabel(SelectedFmSongPoolId)}";

    public string FmCardSubtitle
    {
        get
        {
            if (CurrentFmPreviewSong != null)
                return $"{CurrentFmPreviewSong.Name} · {CurrentFmPreviewSong.Singer}";

            if (!CanUsePersonalFm)
                return "登录后查看猜你喜欢和实时推荐";

            return $"{PersonalFmPresentation.GetSubtitle(SelectedFmMode)} · {PersonalFmPresentation.GetSongPoolLabel(SelectedFmSongPoolId)}";
        }
    }

    public string FmEmptyText
    {
        get
        {
            if (!CanUsePersonalFm)
                return "登录后开启私人 FM";

            return IsFmLoading ? "正在为你更新专属推荐..." : "暂时没有新的私人 FM 推荐";
        }
    }

    public string FmActionHint =>
        !CanUsePersonalFm ? "猜你喜欢和实时推荐需要登录状态" : "实时推荐会根据你的反馈持续更新";

    public string FmStatusCaption
    {
        get
        {
            if (!CanUsePersonalFm)
                return "登录后使用";

            if (IsFmLoading)
                return "正在更新";

            if (!HasPersonalFmSongs)
                return "暂无推荐";

            return "实时推荐";
        }
    }

    public string CompactFmPreviewSongsText
    {
        get
        {
            var previewNames = PersonalFmSongs
                .Skip(1)
                .Take(3)
                .Select(song => song.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();

            return previewNames.Length == 0 ? string.Empty : string.Join(" · ", previewNames);
        }
    }

    public string FmPlayIconPath => IsFmPlaying ? "/Assets/pause-1006-svgrepo-com.svg" : "/Assets/play-1003-svgrepo-com.svg";

    public async Task LoadContentAsync()
    {
        await LoadDailyRecommendationsAsync();
        await LoadPersonalFmPreviewAsync(false, false);
    }

    public async Task OnAuthStateChangedAsync()
    {
        OnPropertyChanged(nameof(CanUsePersonalFm));
        OnPropertyChanged(nameof(FmEmptyText));
        OnPropertyChanged(nameof(FmActionHint));
        OnPropertyChanged(nameof(FmCardSubtitle));
        OnPropertyChanged(nameof(ShowFmLoginState));
        OnPropertyChanged(nameof(ShowFmEmptyState));
        OnPropertyChanged(nameof(ShowFmSongs));
        OnPropertyChanged(nameof(HasPersonalFmSongs));
        OnPropertyChanged(nameof(CanEditFmConfiguration));
        OnPropertyChanged(nameof(ShowFmControls));
        OnPropertyChanged(nameof(ShowCompactFmPreview));
        OnPropertyChanged(nameof(FmStatusCaption));
        OnPropertyChanged(nameof(CompactFmPreviewSongsText));

        if (!CanUsePersonalFm)
        {
            PersonalFmSongs.Clear();
            CurrentFmPreviewSong = null;
            IsFmActive = false;
            RaiseFmCollectionStateChanged();
            _player.ClearPersonalFmSession();
            return;
        }

        await LoadPersonalFmPreviewAsync(false, false);
    }

    [RelayCommand]
    private async Task LoadPersonalFmPreview()
    {
        await LoadPersonalFmPreviewAsync(IsFmActive, false);
    }

    [RelayCommand]
    private async Task ChangePersonalFmMode(PersonalFmMode mode)
    {
        if (SelectedFmMode == mode || IsFmLoading)
            return;

        SelectedFmMode = mode;
        await LoadPersonalFmPreviewAsync(IsFmActive, true);
    }

    [RelayCommand]
    private async Task ChangePersonalFmSongPool(PersonalFmSongPoolId songPoolId)
    {
        if (SelectedFmSongPoolId == songPoolId || IsFmLoading)
            return;

        SelectedFmSongPoolId = songPoolId;
        await LoadPersonalFmPreviewAsync(IsFmActive, true);
    }

    [RelayCommand]
    private async Task CyclePersonalFmMode()
    {
        if (IsFmLoading)
            return;

        SelectedFmMode = SelectedFmMode switch
        {
            PersonalFmMode.Peak => PersonalFmMode.Normal,
            PersonalFmMode.Normal => PersonalFmMode.Small,
            _ => PersonalFmMode.Peak
        };

        await LoadPersonalFmPreviewAsync(IsFmActive, true);
    }

    [RelayCommand]
    private async Task TogglePersonalFmSongPool()
    {
        if (IsFmLoading)
            return;

        SelectedFmSongPoolId = SelectedFmSongPoolId == PersonalFmSongPoolId.Taste
            ? PersonalFmSongPoolId.Style
            : PersonalFmSongPoolId.Taste;

        await LoadPersonalFmPreviewAsync(IsFmActive, true);
    }

    [RelayCommand]
    private async Task PlayPersonalFm()
    {
        if (!CanUsePersonalFm)
        {
            ShowToast("请先登录", "登录后才能使用私人 FM", NotificationType.Warning);
            return;
        }

        if (IsFmActive && _player.CurrentPlayingSong?.PlaybackSource == SongPlaybackSource.PersonalFm)
        {
            _player.TogglePlayPauseCommand.Execute(null);
            OnPropertyChanged(nameof(IsFmPlaying));
            OnPropertyChanged(nameof(FmPlayIconPath));
            return;
        }

        if (PersonalFmSongs.Count == 0)
            await LoadPersonalFmPreviewAsync(false, false);

        if (PersonalFmSongs.Count == 0)
            return;

        await _player.StartPersonalFmAsync(PersonalFmSongs.ToList(), SelectedFmMode, SelectedFmSongPoolId,
            CurrentFmPreviewSong ?? PersonalFmSongs[0]);
        SyncFmStateFromPlayer();
    }

    [RelayCommand]
    private async Task SelectPersonalFmSong(SongItem? song)
    {
        if (song == null || IsFmLoading || !CanUsePersonalFm)
            return;

        await _player.StartPersonalFmAsync(PersonalFmSongs.ToList(), SelectedFmMode, SelectedFmSongPoolId, song);
        SyncFmStateFromPlayer();
    }

    [RelayCommand]
    private async Task DislikePersonalFm()
    {
        if (!IsFmActive || IsFmLoading)
            return;

        var success = await _player.DislikeCurrentPersonalFmAsync();
        if (!success)
            ShowToast("私人 FM", "当前没有可跳转的下一首", NotificationType.Warning);

        SyncFmStateFromPlayer();
    }

    [RelayCommand]
    private async Task RefreshPersonalFm()
    {
        if (IsFmLoading)
            return;

        await LoadPersonalFmPreviewAsync(IsFmActive, true);
    }

    private async Task LoadDailyRecommendationsAsync()
    {
        _logger.LogInformation("正在获取每日推荐...");
        IsDailyRecommendationsLoading = true;
        try
        {
            var response = await _discoveryClient.GetRecommendedSongsAsync();
            if (response?.Songs == null)
                return;

            Songs.Clear();
            Songs.AddRange(response.Songs
                .Select(item => new SongItem
                {
                    Name = item.Name,
                    Singer = item.SingerName,
                    Hash = item.Hash,
                    AlbumId = item.AlbumId,
                    AlbumName = item.AlbumName,
                    AudioId = item.AudioId,
                    Singers = item.Singers,
                    Cover = string.IsNullOrWhiteSpace(item.SizableCover) ? DefaultCover : item.SizableCover,
                    DurationSeconds = item.Duration
                }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取每日推荐失败");
        }
        finally
        {
            IsDailyRecommendationsLoading = false;
        }
    }

    private async Task LoadPersonalFmPreviewAsync(bool restartIfActive, bool showEmptyToast)
    {
        CancelAndDisposeFmPreviewLoad();
        OnPropertyChanged(nameof(CanUsePersonalFm));
        OnPropertyChanged(nameof(FmEmptyText));
        OnPropertyChanged(nameof(FmActionHint));
        OnPropertyChanged(nameof(FmStatusCaption));

        if (!CanUsePersonalFm)
        {
            PersonalFmSongs.Clear();
            CurrentFmPreviewSong = null;
            IsFmActive = false;
            RaiseFmCollectionStateChanged();
            OnPropertyChanged(nameof(FmCardSubtitle));
            return;
        }

        var requestVersion = Interlocked.Increment(ref _fmPreviewRequestVersion);
        var cts = new CancellationTokenSource();
        _fmPreviewLoadCancellation = cts;
        IsFmLoading = true;

        try
        {
            var songs = await _player.FetchPersonalFmPreviewAsync(SelectedFmMode, SelectedFmSongPoolId, cts.Token);
            if (requestVersion != _fmPreviewRequestVersion || cts.IsCancellationRequested)
                return;

            PersonalFmSongs.Clear();
            PersonalFmSongs.AddRange(songs);
            CurrentFmPreviewSong = PersonalFmSongs.FirstOrDefault();
            RaiseFmCollectionStateChanged();

            if (restartIfActive && PersonalFmSongs.Count > 0)
            {
                await _player.StartPersonalFmAsync(PersonalFmSongs.ToList(), SelectedFmMode, SelectedFmSongPoolId,
                    CurrentFmPreviewSong);
                SyncFmStateFromPlayer();
            }
            else
            {
                IsFmActive = _player.IsPersonalFmSessionActive;
            }

            if (showEmptyToast && PersonalFmSongs.Count == 0)
                ShowToast("私人 FM", "暂时没有新的私人 FM 推荐", NotificationType.Information);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载私人 FM 预览失败");
            ShowToast("私人 FM", "加载私人 FM 失败，请稍后再试", NotificationType.Error);
        }
        finally
        {
            if (_fmPreviewLoadCancellation == cts)
                _fmPreviewLoadCancellation = null;

            cts.Dispose();
            IsFmLoading = false;
            OnPropertyChanged(nameof(FmEmptyText));
            OnPropertyChanged(nameof(FmCardSubtitle));
            OnPropertyChanged(nameof(FmPlayIconPath));
            OnPropertyChanged(nameof(ShowFmLoginState));
            OnPropertyChanged(nameof(ShowFmEmptyState));
            OnPropertyChanged(nameof(ShowFmSongs));
            OnPropertyChanged(nameof(CanEditFmConfiguration));
            OnPropertyChanged(nameof(ShowFmControls));
            OnPropertyChanged(nameof(ShowCompactFmPreview));
            OnPropertyChanged(nameof(FmStatusCaption));
            OnPropertyChanged(nameof(CompactFmPreviewSongsText));
        }
    }

    private void SyncFmStateFromPlayer()
    {
        void Update()
        {
            IsFmActive = _player.IsPersonalFmSessionActive;
            if (IsFmActive)
            {
                SelectedFmMode = _player.CurrentPersonalFmMode;
                SelectedFmSongPoolId = _player.CurrentPersonalFmSongPoolId;
                PersonalFmSongs.Clear();
                PersonalFmSongs.AddRange(_player.GetPersonalFmDisplaySongs(5));
                CurrentFmPreviewSong = PersonalFmSongs.FirstOrDefault();
                RaiseFmCollectionStateChanged();
            }

            OnPropertyChanged(nameof(IsFmPlaying));
            OnPropertyChanged(nameof(FmPlayIconPath));
            OnPropertyChanged(nameof(FmCardTitle));
            OnPropertyChanged(nameof(FmCardTagline));
            OnPropertyChanged(nameof(FmCardSubtitle));
            OnPropertyChanged(nameof(FmEmptyText));
            OnPropertyChanged(nameof(ShowFmLoginState));
            OnPropertyChanged(nameof(ShowFmEmptyState));
            OnPropertyChanged(nameof(ShowFmSongs));
            OnPropertyChanged(nameof(CanEditFmConfiguration));
            OnPropertyChanged(nameof(ShowFmControls));
            OnPropertyChanged(nameof(ShowCompactFmPreview));
            OnPropertyChanged(nameof(FmStatusCaption));
            OnPropertyChanged(nameof(CompactFmPreviewSongsText));
        }

        if (Dispatcher.UIThread.CheckAccess())
            Update();
        else
            Dispatcher.UIThread.Post(Update);
    }

    partial void OnSelectedFmModeChanged(PersonalFmMode value)
    {
        OnPropertyChanged(nameof(FmCardTitle));
        OnPropertyChanged(nameof(FmModeShortLabel));
        OnPropertyChanged(nameof(FmCardTagline));
        OnPropertyChanged(nameof(FmCardSubtitle));
        OnPropertyChanged(nameof(FmStatusCaption));
    }

    partial void OnSelectedFmSongPoolIdChanged(PersonalFmSongPoolId value)
    {
        OnPropertyChanged(nameof(FmSongPoolButtonText));
        OnPropertyChanged(nameof(FmCardTagline));
        OnPropertyChanged(nameof(FmCardSubtitle));
    }

    partial void OnCurrentFmPreviewSongChanged(SongItem? value)
    {
        OnPropertyChanged(nameof(FmCardSubtitle));
    }

    private void CancelAndDisposeFmPreviewLoad()
    {
        var cts = Interlocked.Exchange(ref _fmPreviewLoadCancellation, null);
        if (cts == null)
            return;

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        cts.Dispose();
    }

    private void ShowToast(string title, string content, NotificationType type)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _toastManager.CreateToast()
                .OfType(type)
                .WithTitle(title)
                .WithContent(content)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
        });
    }

    private void RaiseFmCollectionStateChanged()
    {
        OnPropertyChanged(nameof(HasPersonalFmSongs));
        OnPropertyChanged(nameof(ShowFmEmptyState));
        OnPropertyChanged(nameof(ShowFmSongs));
        OnPropertyChanged(nameof(ShowFmControls));
        OnPropertyChanged(nameof(ShowCompactFmPreview));
        OnPropertyChanged(nameof(FmStatusCaption));
        OnPropertyChanged(nameof(CompactFmPreviewSongsText));
    }
}
