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
    private static readonly TimeSpan PortraitCycleInterval = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PortraitFadeDuration = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan PortraitPrepareDelay = TimeSpan.FromMilliseconds(80);
    private static readonly IBrush DefaultLyricBrush = new SolidColorBrush(Colors.White);
    private static readonly IBrush DefaultTranslationLineBrush = new SolidColorBrush(Color.Parse("#CCFFFFFF"));
    private static readonly IBrush DefaultTranslationWordBrush = new SolidColorBrush(Colors.White);
    private readonly ILogger<NowPlayingViewModel> _logger;
    private readonly IMainWindowService _mainWindowService;
    private readonly ISongInteractionService _songInteractionService;
    private readonly SongClient _songClient;
    private readonly IUiPreferencesState _uiPreferencesState;
    private CancellationTokenSource? _portraitCancellation;
    private IReadOnlyList<string> _portraitUrls = [];
    private bool _isPortraitLayerAActive = true;
    private int _portraitIndex;
    private bool _disposed;

    public NowPlayingViewModel(
        PlayerViewModel player,
        SongClient songClient,
        ILogger<NowPlayingViewModel> logger,
        IUiPreferencesState uiPreferencesState,
        IMainWindowService mainWindowService,
        ISongInteractionService songInteractionService)
    {
        Player = player;
        _songClient = songClient;
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
        SelectedThemePreset = NowPlayingThemePresetRegistry.Normalize(
            SettingsManager.Settings.NowPlayingThemePreset);
        ApplyUiPreferences(_uiPreferencesState.Current);
        _uiPreferencesState.PropertyChanged += OnUiPreferencesChanged;
    }

    public PlayerViewModel Player { get; }
    public FumeTuningViewModel FumeTuning { get; }
    public MouseParallaxTuningViewModel PendoloParallaxTuning { get; }
    public MouseParallaxTuningViewModel FumeParallaxTuning { get; }

    // The tuning set currently visible in the parallax Flyout; follows the selected
    // theme so each preset keeps its own MaxTilt / Response / Origin.
    public MouseParallaxTuningViewModel ActiveParallaxTuning =>
        SelectedThemePreset switch
        {
            NowPlayingThemePreset.Pendolo => PendoloParallaxTuning,
            NowPlayingThemePreset.Fume => FumeParallaxTuning,
            _ => PendoloParallaxTuning
        };

    public IReadOnlyList<NowPlayingThemePresetOption> ThemePresets =>
        NowPlayingThemePresetRegistry.Presets;

    [ObservableProperty]
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
    public partial bool IsPortraitModeEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsPortraitLoading { get; set; }

    [ObservableProperty]
    public partial bool IsPortraitAvailable { get; set; }

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
    [NotifyPropertyChangedFor(nameof(IsStandardTheme))]
    [NotifyPropertyChangedFor(nameof(IsPendoloTheme))]
    [NotifyPropertyChangedFor(nameof(IsFumeTheme))]
    [NotifyPropertyChangedFor(nameof(IsStandardLayoutVisible))]
    [NotifyPropertyChangedFor(nameof(CurrentThemePresetName))]
    [NotifyPropertyChangedFor(nameof(ActiveParallaxTuning))]
    public partial NowPlayingThemePreset SelectedThemePreset { get; set; } =
        NowPlayingThemePreset.Standard;

    [ObservableProperty]
    public partial FontFamily LyricFontFamily { get; set; } = FontFamily.Default;

    [ObservableProperty]
    public partial double LyricFontSize { get; set; } = 26;

    [ObservableProperty]
    public partial IBrush LyricForeground { get; set; } = DefaultLyricBrush;

    [ObservableProperty]
    public partial HorizontalAlignment LyricHorizontalAlignment { get; set; } = HorizontalAlignment.Center;

    [ObservableProperty]
    public partial TextAlignment LyricTextAlignment { get; set; } = TextAlignment.Center;

    [ObservableProperty]
    public partial double TranslationFontSize { get; set; } = 16;

    [ObservableProperty]
    public partial IBrush TranslationLineForeground { get; set; } = DefaultTranslationLineBrush;

    [ObservableProperty]
    public partial IBrush TranslationWordForeground { get; set; } = DefaultTranslationWordBrush;

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

    public bool IsStandardLayoutVisible => IsStandardTheme && !HasPortraitBackground;

    public string CurrentThemePresetName =>
        NowPlayingThemePresetRegistry.Get(SelectedThemePreset).DisplayName;

    public double BackgroundImageOpacity => HasPortraitBackground ? 0 : 1;

    public string? NowPlayingBackgroundImageSource =>
        BackgroundSource == NowPlayingBackgroundSource.CustomImage &&
        !string.IsNullOrWhiteSpace(CustomBackgroundImagePath)
            ? CustomBackgroundImagePath
            : Player.DisplayedPlayingSong?.Cover;

    public int LyricsGridColumn => HasPortraitBackground ? 0 : 1;

    public int LyricsGridColumnSpan => HasPortraitBackground ? 2 : 1;

    public Thickness LyricsMargin => HasPortraitBackground
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
    private void CycleThemePreset()
    {
        var presets = ThemePresets;
        var currentIndex = 0;
        for (var index = 0; index < presets.Count; index++)
        {
            if (presets[index].Preset != SelectedThemePreset)
                continue;

            currentIndex = index;
            break;
        }

        SelectedThemePreset = presets[(currentIndex + 1) % presets.Count].Preset;
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
        IsPortraitAvailable = false;

        if (IsPortraitModeEnabled)
            _ = RefreshPortraitsAsync();

        _ = PrefetchPortraitAvailabilityAsync();
    }

    private async Task PrefetchPortraitAvailabilityAsync()
    {
        var song = Player.DisplayedPlayingSong;
        if (string.IsNullOrWhiteSpace(song?.Hash))
            return;

        try
        {
            var response = await _songClient.GetAudioImagesAsync(song.Hash, count: 1);
            var urls = ExtractPortraitUrls(response);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Player.DisplayedPlayingSong?.Hash != song.Hash)
                    return;

                IsPortraitAvailable = urls.Count > 0;
                if (!IsPortraitAvailable)
                    IsPortraitModeEnabled = false;
            });
        }
        catch
        {
            // Silently ignore — portrait just won't be available.
        }
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

    partial void OnSelectedThemePresetChanged(NowPlayingThemePreset value)
    {
        var normalized = NowPlayingThemePresetRegistry.Normalize(value);
        if (normalized != value)
        {
            SelectedThemePreset = normalized;
            return;
        }

        if (value is NowPlayingThemePreset.Pendolo or NowPlayingThemePreset.Fume)
            IsPortraitModeEnabled = false;

        SettingsManager.Settings.NowPlayingThemePreset = value;
        SettingsManager.Save();
    }

    private async Task RefreshPortraitsAsync()
    {
        CancelPortraitWork();
        ClearPortraitLayers();

        var song = Player.DisplayedPlayingSong;
        if (!IsPortraitModeEnabled || string.IsNullOrWhiteSpace(song?.Hash))
            return;

        var cancellation = new CancellationTokenSource();
        _portraitCancellation = cancellation;
        var cancellationToken = cancellation.Token;
        IsPortraitLoading = true;

        try
        {
            var response = await _songClient.GetAudioImagesAsync(song.Hash, count: 5);
            cancellationToken.ThrowIfCancellationRequested();

            var urls = ExtractPortraitUrls(response);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested || !IsPortraitModeEnabled)
                    return;

                ApplyPortraitUrls(urls);
                IsPortraitLoading = false;
            });

            if (urls.Count > 1)
                _ = RunPortraitCarouselAsync(urls, cancellationToken);
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

    private async Task RunPortraitCarouselAsync(IReadOnlyList<string> urls, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsPortraitModeEnabled)
            {
                await Task.Delay(PortraitCycleInterval, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested ||
                        !IsPortraitModeEnabled ||
                        !ReferenceEquals(urls, _portraitUrls))
                        return;

                    _portraitIndex = (_portraitIndex + 1) % urls.Count;
                });
                cancellationToken.ThrowIfCancellationRequested();

                await FadeToNextPortraitAsync(urls[_portraitIndex], urls, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyPortraitUrls(IReadOnlyList<string> urls)
    {
        _portraitUrls = urls;
        _portraitIndex = 0;
        _isPortraitLayerAActive = true;

        if (urls.Count == 0)
        {
            ClearPortraitLayers();
            return;
        }

        PortraitBackgroundA = urls[0];
        PortraitBackgroundB = null;
        PortraitLayerAOpacity = 1;
        PortraitLayerBOpacity = 0;
    }

    private async Task FadeToNextPortraitAsync(
        string url,
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!CanApplyPortraitTransition(urls, cancellationToken))
                return;

            PrepareNextPortraitLayer(url);
        }, DispatcherPriority.Render);

        await Task.Delay(PortraitPrepareDelay, cancellationToken);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!CanApplyPortraitTransition(urls, cancellationToken))
                return;

            CommitNextPortraitLayer();
        }, DispatcherPriority.Render);

        await Task.Delay(PortraitFadeDuration, cancellationToken);
    }

    private bool CanApplyPortraitTransition(IReadOnlyList<string> urls, CancellationToken cancellationToken)
    {
        return !cancellationToken.IsCancellationRequested &&
               IsPortraitModeEnabled &&
               ReferenceEquals(urls, _portraitUrls);
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
        _portraitUrls = [];
        _portraitIndex = 0;
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

    private static IReadOnlyList<string> ExtractPortraitUrls(AudioImageResponse? response)
    {
        if (response == null)
            return [];

        var authors = response.Authors.AsValueEnumerable().ToList();
        if (authors.Count == 0)
            return [];

        var urls = new List<string>();
        foreach (var author in authors)
        {
            foreach (var pair in author.Images)
                urls.AddRange(pair.Value.AsValueEnumerable().Select(x => NormalizePortraitUrl(x.SizablePortrait)).ToArray());
        }

        return urls
            .AsValueEnumerable().Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizePortraitUrl(string? url)
    {
        return string.IsNullOrWhiteSpace(url) ? string.Empty : url.Replace("{size}", "800");
    }

    private static Color ParseColorOrDefault(string? colorText, Color fallback)
    {
        return Color.TryParse(colorText, out var parsed) ? parsed : fallback;
    }
}
