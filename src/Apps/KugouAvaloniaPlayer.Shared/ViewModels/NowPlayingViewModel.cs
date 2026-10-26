using System;
using System.Collections.Generic;
using System.ComponentModel;
using ZLinq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class NowPlayingViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan PortraitCycleInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PortraitFadeDuration = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan PortraitPrepareDelay = TimeSpan.FromMilliseconds(80);
    private static readonly IBrush DefaultLyricBrush = new SolidColorBrush(Colors.White);
    private static readonly IBrush DefaultTranslationLineBrush = new SolidColorBrush(Color.Parse("#CCFFFFFF"));
    private static readonly IBrush DefaultTranslationWordBrush = new SolidColorBrush(Colors.White);
    private readonly ILogger<NowPlayingViewModel> _logger;
    private readonly IMainWindowService _mainWindowService;
    private readonly ISongInteractionService _songInteractionService;
    private readonly ArtistClient _artistClient;
    private readonly IUiPreferencesState _uiPreferencesState;
    private CancellationTokenSource? _portraitCancellation;
    private List<List<string>> _portraitGroups = [];
    private bool _isPortraitLayerAActive = true;
    private int[] _portraitGroupIndices = [];
    private int _currentPortraitGroup;
    private bool _disposed;

    public NowPlayingViewModel(
        PlayerViewModel player,
        ArtistClient artistClient,
        ILogger<NowPlayingViewModel> logger,
        IUiPreferencesState uiPreferencesState,
        IMainWindowService mainWindowService,
        ISongInteractionService songInteractionService)
    {
        Player = player;
        _artistClient = artistClient;
        _logger = logger;
        _uiPreferencesState = uiPreferencesState;
        _mainWindowService = mainWindowService;
        _songInteractionService = songInteractionService;
        FumeTuning = new FumeTuningViewModel();
        PendoloParallaxTuning = new MouseParallaxTuningViewModel(
            SettingsManager.Settings.PendoloMouseParallax ??= new MouseParallaxSettings());
        FumeParallaxTuning = new MouseParallaxTuningViewModel(
            SettingsManager.Settings.FumeMouseParallax ??= new MouseParallaxSettings());

        Player.PropertyChanged += OnPlayerPropertyChanged;
        NowPlayingLyricDisplayMode = SettingsManager.Settings.PlayPageLyricDisplayMode;
        FoliaBackgroundMode = Enum.IsDefined(SettingsManager.Settings.FoliaVisualizerBackgroundMode)
            ? SettingsManager.Settings.FoliaVisualizerBackgroundMode
            : FoliaVisualizerBackgroundMode.LatentMaterial;
        SelectedThemePreset = NowPlayingThemePresetRegistry.Normalize(
            SettingsManager.Settings.NowPlayingThemePreset);
        ApplyUiPreferences(_uiPreferencesState.Current);
        _uiPreferencesState.PropertyChanged += OnUiPreferencesChanged;
    }

    public PlayerViewModel Player { get; }
    public FumeTuningViewModel FumeTuning { get; }
    public MouseParallaxTuningViewModel PendoloParallaxTuning { get; }
    public MouseParallaxTuningViewModel FumeParallaxTuning { get; }

    public IReadOnlyList<NowPlayingThemePresetOption> ThemePresets =>
        NowPlayingThemePresetRegistry.Presets;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFoliaThemeActive))]
    [NotifyPropertyChangedFor(nameof(IsFoliaLatentMaterialActive))]
    [NotifyPropertyChangedFor(nameof(IsFoliaBlurredCoverActive))]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial bool IsVolumeVisible { get; set; }

    [ObservableProperty]
    public partial bool IsSingerMenuExpanded { get; set; }

    [ObservableProperty]
    public partial double BackgroundBlurRadius { get; set; } = 40;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NowPlayingBackgroundImageSource))]
    public partial NowPlayingBackgroundSource BackgroundSource { get; set; } =
        NowPlayingBackgroundSource.Cover;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NowPlayingBackgroundImageSource))]
    public partial string? CustomBackgroundImagePath { get; set; }

    [ObservableProperty]
    public partial bool UseLightweightLyricScroll { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PortraitModeStatusText))]
    [NotifyPropertyChangedFor(nameof(IsStandardLayoutVisible))]
    [NotifyPropertyChangedFor(nameof(LyricsGridColumn))]
    [NotifyPropertyChangedFor(nameof(LyricsGridColumnSpan))]
    [NotifyPropertyChangedFor(nameof(LyricsMargin))]
    public partial bool IsPortraitModeEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsPortraitLoading { get; set; }

    [ObservableProperty]
    public partial string? PortraitBackgroundA { get; set; }

    [ObservableProperty]
    public partial string? PortraitBackgroundB { get; set; }

    [ObservableProperty]
    public partial double PortraitLayerAOpacity { get; set; }

    [ObservableProperty]
    public partial double PortraitLayerBOpacity { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPrimaryLyricVisible))]
    [NotifyPropertyChangedFor(nameof(IsTranslationVisible))]
    [NotifyPropertyChangedFor(nameof(IsRomanizationVisible))]
    [NotifyPropertyChangedFor(nameof(CurrentLyricDisplayModeText))]
    public partial NowPlayingLyricDisplayMode NowPlayingLyricDisplayMode { get; set; } =
        NowPlayingLyricDisplayMode.LyricsWithTranslation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFoliaLatentMaterialActive))]
    [NotifyPropertyChangedFor(nameof(IsFoliaBlurredCoverActive))]
    [NotifyPropertyChangedFor(nameof(IsLatentMaterialBackground))]
    [NotifyPropertyChangedFor(nameof(IsBlurredCoverBackground))]
    public partial FoliaVisualizerBackgroundMode FoliaBackgroundMode { get; set; } =
        FoliaVisualizerBackgroundMode.LatentMaterial;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStandardTheme))]
    [NotifyPropertyChangedFor(nameof(IsPendoloTheme))]
    [NotifyPropertyChangedFor(nameof(IsFumeTheme))]
    [NotifyPropertyChangedFor(nameof(IsSonnetTheme))]
    [NotifyPropertyChangedFor(nameof(IsLumiereTheme))]
    [NotifyPropertyChangedFor(nameof(IsTemperaTheme))]
    [NotifyPropertyChangedFor(nameof(IsFoliaThemeActive))]
    [NotifyPropertyChangedFor(nameof(IsFoliaLatentMaterialActive))]
    [NotifyPropertyChangedFor(nameof(IsFoliaBlurredCoverActive))]
    [NotifyPropertyChangedFor(nameof(IsStandardLayoutVisible))]
    [NotifyPropertyChangedFor(nameof(CurrentThemePresetName))]
    public partial NowPlayingThemePreset SelectedThemePreset { get; set; } =
        NowPlayingThemePreset.Standard;

    [ObservableProperty]
    public partial FontFamily LyricFontFamily { get; set; } = FontFamily.Default;

    [ObservableProperty]
    public partial double LyricFontSize { get; set; } = 26;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LyricColor))]
    public partial IBrush LyricForeground { get; set; } = DefaultLyricBrush;

    [ObservableProperty]
    public partial HorizontalAlignment LyricHorizontalAlignment { get; set; } = HorizontalAlignment.Center;

    [ObservableProperty]
    public partial TextAlignment LyricTextAlignment { get; set; } = TextAlignment.Center;

    [ObservableProperty]
    public partial double TranslationFontSize { get; set; } = 16;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TranslationColor))]
    public partial IBrush TranslationLineForeground { get; set; } = DefaultTranslationLineBrush;

    [ObservableProperty]
    public partial IBrush TranslationWordForeground { get; set; } = DefaultTranslationWordBrush;

    public Color LyricColor => (LyricForeground as ISolidColorBrush)?.Color ?? Colors.White;

    public Color TranslationColor => (TranslationLineForeground as ISolidColorBrush)?.Color ?? Colors.White;

    public bool IsPrimaryLyricVisible => true;

    public bool IsTranslationVisible =>
        NowPlayingLyricDisplayMode == NowPlayingLyricDisplayMode.LyricsWithTranslation;

    public bool IsRomanizationVisible =>
        NowPlayingLyricDisplayMode == NowPlayingLyricDisplayMode.LyricsWithRomanization;

    public bool HasCurrentSinger =>
        Player.DisplayedPlayingSong?.Singers.Count > 0;

    public bool CanAddCurrentSongToPlaylist =>
        Player.DisplayedPlayingSong is { LocalFilePath: null };

    public string CurrentLyricDisplayModeText =>
        NowPlayingLyricDisplayMode switch
        {
            NowPlayingLyricDisplayMode.LyricsOnly => "仅歌词",
            NowPlayingLyricDisplayMode.LyricsWithRomanization => "歌词 + 音译",
            _ => "歌词 + 翻译"
        };

    public string PortraitModeStatusText => IsPortraitModeEnabled ? "当前：已开启" : "当前：未开启";

    public string CurrentSingerDisplayText
    {
        get
        {
            var singers = Player.DisplayedPlayingSong?.Singers;
            return singers switch
            {
                { Count: > 1 } => $"{singers.Count} 位歌手",
                { Count: 1 } => singers[0].Name,
                _ => "当前歌手"
            };
        }
    }

    public bool HasPortraitBackground =>
        IsPortraitModeEnabled &&
        (!string.IsNullOrWhiteSpace(PortraitBackgroundA) ||
         !string.IsNullOrWhiteSpace(PortraitBackgroundB));

    public bool IsStandardTheme => SelectedThemePreset == NowPlayingThemePreset.Standard;

    public bool IsPendoloTheme => SelectedThemePreset == NowPlayingThemePreset.Pendolo;

    public bool IsFumeTheme => SelectedThemePreset == NowPlayingThemePreset.Fume;

    public bool IsSonnetTheme => SelectedThemePreset == NowPlayingThemePreset.Sonnet;

    public bool IsLumiereTheme => SelectedThemePreset == NowPlayingThemePreset.Lumiere;

    public bool IsTemperaTheme => SelectedThemePreset == NowPlayingThemePreset.Tempera;

    public bool IsFoliaThemeActive => IsOpen && (IsFumeTheme || IsSonnetTheme || IsLumiereTheme || IsTemperaTheme);

    public bool IsFoliaLatentMaterialActive =>
        IsFoliaThemeActive && FoliaBackgroundMode == FoliaVisualizerBackgroundMode.LatentMaterial;

    public bool IsFoliaBlurredCoverActive =>
        IsFoliaThemeActive && FoliaBackgroundMode == FoliaVisualizerBackgroundMode.BlurredCover;

    public bool IsLatentMaterialBackground =>
        FoliaBackgroundMode == FoliaVisualizerBackgroundMode.LatentMaterial;

    public bool IsBlurredCoverBackground =>
        FoliaBackgroundMode == FoliaVisualizerBackgroundMode.BlurredCover;

    public bool IsStandardLayoutVisible => IsStandardTheme && !IsPortraitModeEnabled;

    public string CurrentThemePresetName =>
        NowPlayingThemePresetRegistry.Get(SelectedThemePreset).DisplayName;

    public double BackgroundImageOpacity => HasPortraitBackground ? 0 : 1;

    public string? NowPlayingBackgroundImageSource =>
        BackgroundSource == NowPlayingBackgroundSource.CustomImage &&
        !string.IsNullOrWhiteSpace(CustomBackgroundImagePath)
            ? CustomBackgroundImagePath
            : Player.DisplayedPlayingSong?.Cover;

    public int LyricsGridColumn => IsPortraitModeEnabled ? 0 : 1;

    public int LyricsGridColumnSpan => IsPortraitModeEnabled ? 2 : 1;

    public Thickness LyricsMargin => IsPortraitModeEnabled
        ? new Thickness(96, 0)
        : new Thickness(60, 0, 0, 0);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancelPortraitWork();
        FumeTuning.Dispose();
        Player.PropertyChanged -= OnPlayerPropertyChanged;
        _uiPreferencesState.PropertyChanged -= OnUiPreferencesChanged;
        GC.SuppressFinalize(this);
    }

    private void OnUiPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(IUiPreferencesState.Current))
            ApplyUiPreferences(_uiPreferencesState.Current);
    }

    private void ApplyUiPreferences(UiPreferencesSnapshot preferences)
    {
        var lyric = preferences.PlayPageLyric;
        BackgroundBlurRadius = preferences.NowPlayingBackgroundBlurRadius;
        BackgroundSource = preferences.NowPlayingBackgroundSource;
        UseLightweightLyricScroll = preferences.UseLightweightNowPlayingLyricScroll;
        CustomBackgroundImagePath = preferences.AppBackground.CustomImagePath;
        ApplyLyricStyleSettings(
            lyric.UseCustomMainColor,
            lyric.MainColorHex,
            lyric.UseCustomTranslationColor,
            lyric.TranslationColorHex,
            lyric.UseCustomFont,
            lyric.FontFamilyName,
            lyric.Alignment,
            lyric.FontSize);
    }

    [RelayCommand]
    private void Open()
    {
        IsOpen = true;
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        IsSingerMenuExpanded = false;
        IsVolumeVisible = false;
        IsPortraitModeEnabled = false;
    }

    [RelayCommand]
    private void ToggleVolume()
    {
        IsVolumeVisible = !IsVolumeVisible;
    }

    [RelayCommand]
    private void ToggleSingerMenu()
    {
        IsSingerMenuExpanded = !IsSingerMenuExpanded;
    }

    [RelayCommand]
    private void ToggleLyricDisplayMode()
    {
        NowPlayingLyricDisplayMode = NowPlayingLyricDisplayMode switch
        {
            NowPlayingLyricDisplayMode.LyricsWithTranslation => NowPlayingLyricDisplayMode.LyricsOnly,
            NowPlayingLyricDisplayMode.LyricsOnly => NowPlayingLyricDisplayMode.LyricsWithRomanization,
            _ => NowPlayingLyricDisplayMode.LyricsWithTranslation
        };
    }

    [RelayCommand]
    private void TogglePortraitMode()
    {
        IsPortraitModeEnabled = !IsPortraitModeEnabled;
    }

    [RelayCommand]
    private void ViewSinger(SingerLite? singer)
    {
        if (singer != null)
        {
            Close();
            _songInteractionService.NavigateToSinger(singer);
        }
    }

    [RelayCommand]
    private void AddCurrentSongToPlaylist()
    {
        var song = Player.DisplayedPlayingSong;
        if (song != null && song.LocalFilePath is null)
            _ = _songInteractionService.ShowAddToPlaylistDialogAsync(song);
    }

    [RelayCommand]
    private void RequestWindowMinimize()
    {
        _mainWindowService.Minimize();
    }

    [RelayCommand]
    private void RequestWindowToggleFullScreen()
    {
        _mainWindowService.ToggleFullScreen();
    }

    [RelayCommand]
    private void RequestWindowToggleMaximize()
    {
        _mainWindowService.ToggleMaximize();
    }

    [RelayCommand]
    private void RequestWindowClose()
    {
        _mainWindowService.Close();
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PlayerViewModel.DisplayedPlayingSong))
            return;

        OnPropertyChanged(nameof(HasCurrentSinger));
        OnPropertyChanged(nameof(CanAddCurrentSongToPlaylist));
        OnPropertyChanged(nameof(CurrentSingerDisplayText));
        OnPropertyChanged(nameof(NowPlayingBackgroundImageSource));
        IsSingerMenuExpanded = false;

        if (IsPortraitModeEnabled)
            _ = RefreshPortraitsAsync();
    }

    partial void OnIsPortraitModeEnabledChanged(bool value)
    {
        if (!value)
        {
            ClearPortraitState();
            return;
        }

        IsVolumeVisible = false;
        _ = RefreshPortraitsAsync();
        NotifyPortraitLayoutProperties();
    }

    partial void OnPortraitBackgroundAChanged(string? value)
    {
        NotifyPortraitLayoutProperties();
    }

    partial void OnPortraitBackgroundBChanged(string? value)
    {
        NotifyPortraitLayoutProperties();
    }

    partial void OnNowPlayingLyricDisplayModeChanged(NowPlayingLyricDisplayMode value)
    {
        SettingsManager.Settings.PlayPageLyricDisplayMode = value;
        SettingsManager.Save();
    }

    partial void OnFoliaBackgroundModeChanged(FoliaVisualizerBackgroundMode value)
    {
        var normalized = Enum.IsDefined(value)
            ? value
            : FoliaVisualizerBackgroundMode.LatentMaterial;
        if (normalized != value)
        {
            FoliaBackgroundMode = normalized;
            return;
        }

        SettingsManager.Settings.FoliaVisualizerBackgroundMode = normalized;
        SettingsManager.Save();
    }

    [RelayCommand]
    private void UseLatentMaterialBackground() =>
        FoliaBackgroundMode = FoliaVisualizerBackgroundMode.LatentMaterial;

    [RelayCommand]
    private void UseBlurredCoverBackground() =>
        FoliaBackgroundMode = FoliaVisualizerBackgroundMode.BlurredCover;

    partial void OnSelectedThemePresetChanged(NowPlayingThemePreset value)
    {
        var normalized = NowPlayingThemePresetRegistry.Normalize(value);
        if (normalized != value)
        {
            SelectedThemePreset = normalized;
            return;
        }

        if (value is NowPlayingThemePreset.Pendolo or NowPlayingThemePreset.Fume or NowPlayingThemePreset.Sonnet or NowPlayingThemePreset.Lumiere or NowPlayingThemePreset.Tempera)
            IsPortraitModeEnabled = false;

        SettingsManager.Settings.NowPlayingThemePreset = value;
        SettingsManager.Save();
    }

    private async Task RefreshPortraitsAsync()
    {
        CancelPortraitWork();
        ClearPortraitLayers();

        var song = Player.DisplayedPlayingSong;
        if (!IsPortraitModeEnabled || song == null || !song.Singers.Exists(s => s.Id > 0))
            return;

        var cancellation = new CancellationTokenSource();
        _portraitCancellation = cancellation;
        var cancellationToken = cancellation.Token;
        IsPortraitLoading = true;

        try
        {
            var groups = await FetchPortraitUrlsAsync(song, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested || !IsPortraitModeEnabled)
                    return;

                ApplyPortraitUrls(groups);
                IsPortraitLoading = false;
            });

            var totalImages = 0;
            foreach (var group in groups)
                totalImages += group.Count;
            if (totalImages > 1)
                _ = RunPortraitCarouselAsync(groups, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载 NowPlayingView 写真失败");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                    ClearPortraitState();
            });
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                IsPortraitLoading = false;
        }
    }

    private async Task RunPortraitCarouselAsync(List<List<string>> groups, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsPortraitModeEnabled)
            {
                await Task.Delay(PortraitCycleInterval, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                string? next = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested ||
                        !IsPortraitModeEnabled ||
                        !ReferenceEquals(groups, _portraitGroups))
                        return;

                    // 多歌手交替策略：每个歌手轮流出一张，避免单歌手写真连续霸屏。
                    next = GetNextAlternatingPortraitUrl();
                });
                if (next == null)
                    continue;

                cancellationToken.ThrowIfCancellationRequested();
                await FadeToNextPortraitAsync(next, groups, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    ///     交替轮播取图：从当前歌手开始轮转，每个歌手按各自索引取一张；
    ///     若取到的图与当前显示图相同（如首张已被 ApplyPortraitUrls 展示），则继续取下一张。
    /// </summary>
    private string? GetNextAlternatingPortraitUrl()
    {
        if (_portraitGroups.Count == 0)
            return null;

        var current = _isPortraitLayerAActive ? PortraitBackgroundA : PortraitBackgroundB;

        for (var attempt = 0; attempt <= _portraitGroups.Count; attempt++)
        {
            for (var i = 0; i < _portraitGroups.Count; i++)
            {
                var groupIndex = (_currentPortraitGroup + i) % _portraitGroups.Count;
                var images = _portraitGroups[groupIndex];
                if (images.Count == 0)
                    continue;

                var url = images[_portraitGroupIndices[groupIndex]];
                _portraitGroupIndices[groupIndex] = (_portraitGroupIndices[groupIndex] + 1) % images.Count;
                _currentPortraitGroup = (groupIndex + 1) % _portraitGroups.Count;

                if (!string.Equals(url, current, StringComparison.Ordinal))
                    return url;
            }
        }

        return null;
    }

    private void ApplyPortraitUrls(List<List<string>> groups)
    {
        _portraitGroups = groups;
        _portraitGroupIndices = new int[groups.Count];
        _currentPortraitGroup = 0;
        _isPortraitLayerAActive = true;

        var flat = new List<string>();
        foreach (var group in groups)
            flat.AddRange(group);
        if (flat.Count == 0)
        {
            ClearPortraitLayers();
            return;
        }

        PortraitBackgroundA = flat[0];
        PortraitBackgroundB = null;
        PortraitLayerAOpacity = 1;
        PortraitLayerBOpacity = 0;
    }

    private async Task FadeToNextPortraitAsync(
        string url,
        List<List<string>> groups,
        CancellationToken cancellationToken)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!CanApplyPortraitTransition(groups, cancellationToken))
                return;

            PrepareNextPortraitLayer(url);
        }, DispatcherPriority.Render);

        await Task.Delay(PortraitPrepareDelay, cancellationToken);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!CanApplyPortraitTransition(groups, cancellationToken))
                return;

            CommitNextPortraitLayer();
        }, DispatcherPriority.Render);

        await Task.Delay(PortraitFadeDuration, cancellationToken);
    }

    private bool CanApplyPortraitTransition(List<List<string>> groups, CancellationToken cancellationToken)
    {
        return !cancellationToken.IsCancellationRequested &&
               IsPortraitModeEnabled &&
               ReferenceEquals(groups, _portraitGroups);
    }

    private void PrepareNextPortraitLayer(string url)
    {
        if (_isPortraitLayerAActive)
        {
            PortraitBackgroundB = url;
            PortraitLayerBOpacity = 0;
        }
        else
        {
            PortraitBackgroundA = url;
            PortraitLayerAOpacity = 0;
        }
    }

    private void CommitNextPortraitLayer()
    {
        if (_isPortraitLayerAActive)
        {
            PortraitLayerBOpacity = 1;
            PortraitLayerAOpacity = 0;
        }
        else
        {
            PortraitLayerAOpacity = 1;
            PortraitLayerBOpacity = 0;
        }

        _isPortraitLayerAActive = !_isPortraitLayerAActive;
    }

    private void ClearPortraitState()
    {
        CancelPortraitWork();
        ClearPortraitLayers();
        IsPortraitLoading = false;
        NotifyPortraitLayoutProperties();
    }

    private void ClearPortraitLayers()
    {
        _portraitGroups = [];
        _portraitGroupIndices = [];
        _currentPortraitGroup = 0;
        _isPortraitLayerAActive = true;
        PortraitBackgroundA = null;
        PortraitBackgroundB = null;
        PortraitLayerAOpacity = 0;
        PortraitLayerBOpacity = 0;
    }

    private void CancelPortraitWork()
    {
        var cancellation = _portraitCancellation;
        if (cancellation == null)
            return;

        _portraitCancellation = null;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private void NotifyPortraitLayoutProperties()
    {
        OnPropertyChanged(nameof(HasPortraitBackground));
        OnPropertyChanged(nameof(IsStandardLayoutVisible));
        OnPropertyChanged(nameof(BackgroundImageOpacity));
        OnPropertyChanged(nameof(LyricsGridColumn));
        OnPropertyChanged(nameof(LyricsGridColumnSpan));
        OnPropertyChanged(nameof(LyricsMargin));
    }

    private void ApplyLyricStyleSettings(
        bool useCustomMainColor,
        string mainColorHex,
        bool useCustomTranslationColor,
        string translationColorHex,
        bool useCustomFont,
        string fontFamilyName,
        LyricAlignmentOption alignment,
        double fontSize)
    {
        ApplyFontSettings(useCustomFont, fontFamilyName);
        ApplyAlignmentSettings(alignment);
        ApplyFontSizeSettings(fontSize);

        LyricForeground = useCustomMainColor
            ? new SolidColorBrush(ParseColorOrDefault(mainColorHex, Colors.White))
            : DefaultLyricBrush;

        if (useCustomTranslationColor)
        {
            var color = new SolidColorBrush(ParseColorOrDefault(translationColorHex, Color.Parse("#CCFFFFFF")));
            TranslationLineForeground = color;
            TranslationWordForeground = color;
            return;
        }

        TranslationLineForeground = DefaultTranslationLineBrush;
        TranslationWordForeground = DefaultTranslationWordBrush;
    }

    private void ApplyFontSettings(bool useCustomFont, string fontFamilyName)
    {
        LyricFontFamily = AppFontService.ResolveEffectiveLyricFontFamily(useCustomFont, fontFamilyName);
    }

    private void ApplyAlignmentSettings(LyricAlignmentOption alignment)
    {
        switch (alignment)
        {
            case LyricAlignmentOption.Left:
                LyricHorizontalAlignment = HorizontalAlignment.Left;
                LyricTextAlignment = TextAlignment.Left;
                break;
            case LyricAlignmentOption.Right:
                LyricHorizontalAlignment = HorizontalAlignment.Right;
                LyricTextAlignment = TextAlignment.Right;
                break;
            default:
                LyricHorizontalAlignment = HorizontalAlignment.Center;
                LyricTextAlignment = TextAlignment.Center;
                break;
        }
    }

    private void ApplyFontSizeSettings(double fontSize)
    {
        var clamped = Math.Clamp(fontSize, 18, 42);
        LyricFontSize = clamped;
        TranslationFontSize = Math.Max(14, Math.Round(clamped * 0.62, 1));
    }

    /// <summary>
    ///     按歌手 ID 获取写真（酷狗开放平台 /kmr/v1/author/extend），支持多歌手合并与去重。
    /// </summary>
    private async Task<List<List<string>>> FetchPortraitUrlsAsync(
        SongItem song,
        CancellationToken cancellationToken = default)
    {
        var authorIds = new List<long>();
        foreach (var singer in song.Singers)
        {
            if (singer.Id > 0 && !authorIds.Contains(singer.Id))
                authorIds.Add(singer.Id);
        }
        if (authorIds.Count == 0)
            return [];

        var tasks = new List<Task<List<string>>>(authorIds.Count);
        foreach (var authorId in authorIds)
            tasks.Add(FetchAuthorPortraitUrlsAsync(authorId, cancellationToken));

        var results = await Task.WhenAll(tasks);

        // 每个歌手一组，组内去重；跳过没有写真的歌手，保证组序与歌手顺序一致。
        var groups = new List<List<string>>();
        foreach (var group in results)
        {
            var distinct = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var url in group)
            {
                if (!string.IsNullOrWhiteSpace(url) && seen.Add(url))
                    distinct.Add(url);
            }

            if (distinct.Count > 0)
                groups.Add(distinct);
        }

        return groups;
    }

    private async Task<List<string>> FetchAuthorPortraitUrlsAsync(long authorId, CancellationToken cancellationToken)
    {
        var urls = new List<string>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = await _artistClient.GetExtendImagesAsync(authorId.ToString());
            if (items == null)
                return urls;

            foreach (var item in items)
            {
                foreach (var image in item.Images)
                {
                    if (string.IsNullOrWhiteSpace(image.File))
                        continue;

                    // singerimg.kugou.com 的 http 地址会被 .NET HttpClient 403 拒绝，统一升级为 https。
                    var file = image.File.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        ? "https://" + image.File[7..]
                        : image.File;
                    urls.Add(file);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取歌手写真失败，authorId={AuthorId}", authorId);
        }

        return urls;
    }

    private static Color ParseColorOrDefault(string? colorText, Color fallback)
    {
        return Color.TryParse(colorText, out var parsed) ? parsed : fallback;
    }
}
