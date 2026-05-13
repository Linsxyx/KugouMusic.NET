using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
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
    private readonly SongClient _songClient;
    private CancellationTokenSource? _portraitCancellation;
    private IReadOnlyList<string> _portraitUrls = [];
    private bool _isPortraitLayerAActive = true;
    private int _portraitIndex;
    private bool _disposed;

    public NowPlayingViewModel(PlayerViewModel player, SongClient songClient, ILogger<NowPlayingViewModel> logger)
    {
        Player = player;
        _songClient = songClient;
        _logger = logger;

        Player.PropertyChanged += OnPlayerPropertyChanged;
        NowPlayingLyricDisplayMode = SettingsManager.Settings.PlayPageLyricDisplayMode;
        BackgroundOpacity = Math.Clamp(SettingsManager.Settings.NowPlayingBackgroundOpacity, 0.0, 1.0);
        ApplyLyricStyleSettings(
            SettingsManager.Settings.PlayPageLyricUseCustomMainColor,
            SettingsManager.Settings.PlayPageLyricCustomMainColor,
            SettingsManager.Settings.PlayPageLyricUseCustomTranslationColor,
            SettingsManager.Settings.PlayPageLyricCustomTranslationColor,
            SettingsManager.Settings.PlayPageLyricUseCustomFont,
            SettingsManager.Settings.PlayPageLyricCustomFontFamily,
            SettingsManager.Settings.PlayPageLyricAlignment,
            SettingsManager.Settings.PlayPageLyricFontSize);

        WeakReferenceMessenger.Default.Register<LyricStyleSettingsChangedMessage>(this, (_, message) =>
        {
            if (message.Scope != LyricSettingsScope.PlayPage)
                return;

            ApplyLyricStyleSettings(
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

        WeakReferenceMessenger.Default.Register<NowPlayingBackgroundOpacityChangedMessage>(this, (_, message) =>
        {
            BackgroundOpacity = message.Opacity;
        });
    }

    public PlayerViewModel Player { get; }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial bool IsVolumeVisible { get; set; }

    [ObservableProperty]
    public partial bool EnableLegacyWordLyricEffect { get; set; } =
        SettingsManager.Settings.EnableLegacyWordLyricEffect;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CoverBackgroundOpacity))]
    public partial double BackgroundOpacity { get; set; } = 0.5;

    [ObservableProperty]
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
    public partial NowPlayingLyricDisplayMode NowPlayingLyricDisplayMode { get; set; } =
        NowPlayingLyricDisplayMode.LyricsWithTranslation;

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

    public bool HasPortraitBackground =>
        IsPortraitModeEnabled &&
        (!string.IsNullOrWhiteSpace(PortraitBackgroundA) ||
         !string.IsNullOrWhiteSpace(PortraitBackgroundB));

    public bool IsStandardLayoutVisible => !HasPortraitBackground;

    public double CoverBackgroundOpacity => HasPortraitBackground ? 0 : BackgroundOpacity;

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
        Player.PropertyChanged -= OnPlayerPropertyChanged;
        WeakReferenceMessenger.Default.UnregisterAll(this);
        GC.SuppressFinalize(this);
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
        IsVolumeVisible = false;
        IsPortraitModeEnabled = false;
    }

    [RelayCommand]
    private void ToggleVolume()
    {
        IsVolumeVisible = !IsVolumeVisible;
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

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PlayerViewModel.DisplayedPlayingSong))
            return;

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
            _logger.LogWarning(ex, "加载 NowPlaying 写真失败");
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
        OnPropertyChanged(nameof(CoverBackgroundOpacity));
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
        if (!useCustomFont || string.IsNullOrWhiteSpace(fontFamilyName))
        {
            LyricFontFamily = FontFamily.Default;
            return;
        }

        LyricFontFamily = IsSystemFontInstalled(fontFamilyName)
            ? new FontFamily($"{fontFamilyName}, Microsoft YaHei, SimSun")
            : FontFamily.Default;
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

        var authors = response.Authors.ToList();
        if (authors.Count == 0)
            return [];

        var urls = new List<string>();
        foreach (var author in authors)
        {
            foreach (var pair in author.Images)
                urls.AddRange(pair.Value.Select(x => NormalizePortraitUrl(x.SizablePortrait)));
        }

        return urls
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizePortraitUrl(string? url)
    {
        return string.IsNullOrWhiteSpace(url) ? string.Empty : url.Replace("{size}", "800");
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
}
