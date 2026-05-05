using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class DesktopLyricViewModel : ViewModelBase, IDisposable
{
    private const double MinFontSize = 18;
    private const double MaxFontSize = 50;
    private const double FontSizeStep = 2;
    private const double ControlBarReservedHeight = 64;
    private const double MinWindowHeight = 140;
    private const double WindowVerticalPadding = 24;

    private static readonly IBrush DefaultLyricBrush = new SolidColorBrush(Colors.White);
    private static readonly IBrush DefaultTranslationLineBrush = new SolidColorBrush(Color.Parse("#CCFFFFFF"));
    private static readonly IBrush DefaultTranslationWordBrush = new SolidColorBrush(Colors.White);

    [ObservableProperty]
    public partial double FontSize { get; set; } = 30;

    [ObservableProperty]
    public partial bool IsLocked { get; set; }

    [ObservableProperty]
    public partial bool IsControlBarExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsControlHotspotHovered { get; set; }

    [ObservableProperty]
    public partial bool IsCollapsedLockIconHovered { get; set; }

    [ObservableProperty]
    public partial bool EnableLegacyWordLyricEffect { get; set; }

    [ObservableProperty]
    public partial bool IsTranslationVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsDoubleLineEnabled { get; set; }

    [ObservableProperty] public partial FontFamily LyricFontFamily { get; set; } = FontFamily.Default;

    [ObservableProperty]
    public partial IBrush LyricForeground { get; set; } = DefaultLyricBrush;

    [ObservableProperty]
    public partial double TranslationFontSize { get; set; } = 18;

    [ObservableProperty]
    public partial IBrush TranslationLineForeground { get; set; } = DefaultTranslationLineBrush;

    [ObservableProperty]
    public partial IBrush TranslationWordForeground { get; set; } = DefaultTranslationWordBrush;

    [ObservableProperty]
    public partial LyricLineViewModel? TopLyricLine { get; set; }

    [ObservableProperty]
    public partial LyricLineViewModel? BottomLyricLine { get; set; }

    [ObservableProperty]
    public partial bool IsTopLyricLineCurrent { get; set; }

    [ObservableProperty]
    public partial bool IsBottomLyricLineCurrent { get; set; }

    [ObservableProperty]
    public partial double TopLaneOpacity { get; set; } = 1;

    [ObservableProperty]
    public partial double BottomLaneOpacity { get; set; } = 1;

    [ObservableProperty]
    public partial double TopLaneTranslateY { get; set; }

    [ObservableProperty]
    public partial double BottomLaneTranslateY { get; set; }

    private CancellationTokenSource? _topLaneAnimationCancellation;
    private CancellationTokenSource? _bottomLaneAnimationCancellation;

    public DesktopLyricViewModel(PlayerViewModel player, bool canMousePassthrough, bool usesSeparateLockOverlay)
    {
        Player = player;
        Player.PropertyChanged += OnPlayerPropertyChanged;
        CanMousePassthrough = canMousePassthrough;
        UsesSeparateLockOverlay = canMousePassthrough && usesSeparateLockOverlay;
        IsControlBarExpanded = false;
        EnableLegacyWordLyricEffect = SettingsManager.Settings.EnableLegacyWordLyricEffect;
        FontSize = ClampFontSize(SettingsManager.Settings.DesktopLyricFontSize);
        IsTranslationVisible = SettingsManager.Settings.DesktopLyricShowTranslation;
        IsDoubleLineEnabled = SettingsManager.Settings.DesktopLyricDoubleLineEnabled;
        ApplyLyricStyleSettings(
            SettingsManager.Settings.DesktopLyricUseCustomMainColor,
            SettingsManager.Settings.DesktopLyricCustomMainColor,
            SettingsManager.Settings.DesktopLyricUseCustomTranslationColor,
            SettingsManager.Settings.DesktopLyricCustomTranslationColor,
            SettingsManager.Settings.DesktopLyricUseCustomFont,
            SettingsManager.Settings.DesktopLyricCustomFontFamily);

        WeakReferenceMessenger.Default.Register<LyricStyleSettingsChangedMessage>(this, (_, message) =>
        {
            if (message.Scope != LyricSettingsScope.Desktop)
                return;

            ApplyLyricStyleSettings(
                message.UseCustomMainColor,
                message.MainColorHex,
                message.UseCustomTranslationColor,
                message.TranslationColorHex,
                message.UseCustomFont,
                message.FontFamilyName);
            EnableLegacyWordLyricEffect = message.EnableLegacyWordLyricEffect;
        });

        WeakReferenceMessenger.Default.Register<DesktopLyricDoubleLineChangedMessage>(this, (_, message) =>
        {
            IsDoubleLineEnabled = message.IsEnabled;
        });

        RefreshDoubleLineLanes();
    }

    public bool CanMousePassthrough { get; }
    public bool UsesSeparateLockOverlay { get; }

    public PlayerViewModel Player { get; }

    public string FontSizeDisplay => $"{Math.Round(FontSize):0}pt";
    public double WindowHeight => CalculateWindowHeight();
    public bool IsUnlockedInteractionEnabled => !IsLocked;
    public bool IsCollapsedLockIconVisible => CanMousePassthrough && IsLocked;
    public bool IsEmbeddedCollapsedLockIconVisible => IsCollapsedLockIconVisible && !UsesSeparateLockOverlay;
    public bool IsSingleLineMode => !IsDoubleLineEnabled;
    public bool IsDesktopTranslationActuallyVisible => IsTranslationVisible && !IsDoubleLineEnabled;
    public bool IsTopLyricLineVisible => IsDoubleLineEnabled && TopLyricLine != null;
    public bool IsBottomLyricLineVisible => IsDoubleLineEnabled && BottomLyricLine != null;
    public bool IsTopPlainTextVisible =>
        IsTopLyricLineVisible && (!IsTopLyricLineCurrent || TopLyricLine?.IsKrcWordLevel != true);
    public bool IsBottomPlainTextVisible =>
        IsBottomLyricLineVisible && (!IsBottomLyricLineCurrent || BottomLyricLine?.IsKrcWordLevel != true);
    public bool IsTopLegacyWordsVisible =>
        IsTopLyricLineVisible && IsTopLyricLineCurrent && TopLyricLine?.IsKrcWordLevel == true &&
        EnableLegacyWordLyricEffect;
    public bool IsBottomLegacyWordsVisible =>
        IsBottomLyricLineVisible && IsBottomLyricLineCurrent && BottomLyricLine?.IsKrcWordLevel == true &&
        EnableLegacyWordLyricEffect;
    public bool IsTopGradientWordsVisible =>
        IsTopLyricLineVisible && IsTopLyricLineCurrent && TopLyricLine?.IsKrcWordLevel == true &&
        !EnableLegacyWordLyricEffect;
    public bool IsBottomGradientWordsVisible =>
        IsBottomLyricLineVisible && IsBottomLyricLineCurrent && BottomLyricLine?.IsKrcWordLevel == true &&
        !EnableLegacyWordLyricEffect;

    [RelayCommand]
    private void ToggleLock()
    {
        IsLocked = !IsLocked;
    }

    [RelayCommand]
    private void IncreaseFontSize()
    {
        FontSize = ClampFontSize(FontSize + FontSizeStep);
    }

    [RelayCommand]
    private void DecreaseFontSize()
    {
        FontSize = ClampFontSize(FontSize - FontSizeStep);
    }

    [RelayCommand]
    private void ToggleTranslationVisibility()
    {
        IsTranslationVisible = !IsTranslationVisible;
    }

    partial void OnFontSizeChanged(double value)
    {
        var clamped = ClampFontSize(value);
        if (Math.Abs(clamped - value) > double.Epsilon)
        {
            FontSize = clamped;
            return;
        }

        TranslationFontSize = Math.Max(14, Math.Round(value * 0.6, 1));
        SettingsManager.Settings.DesktopLyricFontSize = value;
        SettingsManager.Save();
        OnPropertyChanged(nameof(FontSizeDisplay));
        OnPropertyChanged(nameof(WindowHeight));
    }

    partial void OnIsTranslationVisibleChanged(bool value)
    {
        SettingsManager.Settings.DesktopLyricShowTranslation = value;
        SettingsManager.Save();
        OnPropertyChanged(nameof(IsDesktopTranslationActuallyVisible));
        OnPropertyChanged(nameof(WindowHeight));
    }

    partial void OnIsDoubleLineEnabledChanged(bool value)
    {
        SettingsManager.Settings.DesktopLyricDoubleLineEnabled = value;
        SettingsManager.Save();
        OnPropertyChanged(nameof(IsSingleLineMode));
        OnPropertyChanged(nameof(IsDesktopTranslationActuallyVisible));
        OnPropertyChanged(nameof(WindowHeight));
        RefreshDoubleLineLanes();
    }

    partial void OnEnableLegacyWordLyricEffectChanged(bool value)
    {
        RaiseDoubleLineComputedProperties();
    }

    partial void OnIsLockedChanged(bool value)
    {
        if (value)
        {
            IsControlBarExpanded = false;
            IsControlHotspotHovered = false;
        }
        else
        {
            IsControlBarExpanded = false;
            IsControlHotspotHovered = false;
            IsCollapsedLockIconHovered = false;
        }

        OnPropertyChanged(nameof(IsUnlockedInteractionEnabled));
        OnPropertyChanged(nameof(IsCollapsedLockIconVisible));
        OnPropertyChanged(nameof(IsEmbeddedCollapsedLockIconVisible));
    }

    partial void OnIsControlHotspotHoveredChanged(bool value)
    {
        if (IsLocked)
            return;

        IsControlBarExpanded = value;
    }

    public void SetControlHotspotHovered(bool value)
    {
        IsControlHotspotHovered = value;
    }

    public void SetCollapsedLockIconHovered(bool value)
    {
        if (!CanMousePassthrough || !IsLocked)
        {
            IsCollapsedLockIconHovered = false;
            return;
        }

        IsCollapsedLockIconHovered = value;
    }

    public void Unlock()
    {
        IsLocked = false;
        IsControlBarExpanded = true;
        IsControlHotspotHovered = true;
    }

    public void Dispose()
    {
        CancelAndDisposeLaneAnimations();
        Player.PropertyChanged -= OnPlayerPropertyChanged;
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Player.CurrentLyricLine) or nameof(Player.CurrentLyricIndex) or
            nameof(Player.NextLyricLine))
            RefreshDoubleLineLanes();
    }

    private void RefreshDoubleLineLanes()
    {
        var currentLine = Player.CurrentLyricLine;
        var currentIndex = Player.CurrentLyricIndex;

        if (!IsDoubleLineEnabled || currentLine == null || currentIndex < 0)
        {
            SetTopLaneImmediate(null, false);
            SetBottomLaneImmediate(null, false);
            return;
        }

        var nextLine = Player.NextLyricLine;
        if (currentIndex % 2 == 0)
        {
            SetTopLane(currentLine, true);
            SetBottomLane(nextLine, false);
        }
        else
        {
            SetTopLane(nextLine, false);
            SetBottomLane(currentLine, true);
        }
    }

    private void SetTopLane(LyricLineViewModel? line, bool isCurrent)
    {
        if (ReferenceEquals(TopLyricLine, line))
        {
            IsTopLyricLineCurrent = isCurrent;
            RaiseDoubleLineComputedProperties();
            return;
        }

        if (TopLyricLine == null || line == null)
        {
            SetTopLaneImmediate(line, isCurrent);
            return;
        }

        _ = AnimateTopLaneChangeAsync(line, isCurrent);
    }

    private void SetBottomLane(LyricLineViewModel? line, bool isCurrent)
    {
        if (ReferenceEquals(BottomLyricLine, line))
        {
            IsBottomLyricLineCurrent = isCurrent;
            RaiseDoubleLineComputedProperties();
            return;
        }

        if (BottomLyricLine == null || line == null)
        {
            SetBottomLaneImmediate(line, isCurrent);
            return;
        }

        _ = AnimateBottomLaneChangeAsync(line, isCurrent);
    }

    private void SetTopLaneImmediate(LyricLineViewModel? line, bool isCurrent)
    {
        CancelAndDisposeTopLaneAnimation();
        TopLyricLine = line;
        IsTopLyricLineCurrent = isCurrent;
        TopLaneOpacity = 1;
        TopLaneTranslateY = 0;
        RaiseDoubleLineComputedProperties();
    }

    private void SetBottomLaneImmediate(LyricLineViewModel? line, bool isCurrent)
    {
        CancelAndDisposeBottomLaneAnimation();
        BottomLyricLine = line;
        IsBottomLyricLineCurrent = isCurrent;
        BottomLaneOpacity = 1;
        BottomLaneTranslateY = 0;
        RaiseDoubleLineComputedProperties();
    }

    private async Task AnimateTopLaneChangeAsync(LyricLineViewModel line, bool isCurrent)
    {
        CancelAndDisposeTopLaneAnimation();
        var cts = new CancellationTokenSource();
        _topLaneAnimationCancellation = cts;

        try
        {
            TopLaneOpacity = 0;
            TopLaneTranslateY = -8;
            await Task.Delay(120, cts.Token);

            TopLyricLine = line;
            IsTopLyricLineCurrent = isCurrent;
            RaiseDoubleLineComputedProperties();
            TopLaneTranslateY = 8;
            await Task.Delay(16, cts.Token);

            TopLaneOpacity = 1;
            TopLaneTranslateY = 0;
        }
        catch (TaskCanceledException)
        {
        }
    }

    private async Task AnimateBottomLaneChangeAsync(LyricLineViewModel line, bool isCurrent)
    {
        CancelAndDisposeBottomLaneAnimation();
        var cts = new CancellationTokenSource();
        _bottomLaneAnimationCancellation = cts;

        try
        {
            BottomLaneOpacity = 0;
            BottomLaneTranslateY = 8;
            await Task.Delay(120, cts.Token);

            BottomLyricLine = line;
            IsBottomLyricLineCurrent = isCurrent;
            RaiseDoubleLineComputedProperties();
            BottomLaneTranslateY = -8;
            await Task.Delay(16, cts.Token);

            BottomLaneOpacity = 1;
            BottomLaneTranslateY = 0;
        }
        catch (TaskCanceledException)
        {
        }
    }

    private void CancelAndDisposeLaneAnimations()
    {
        CancelAndDisposeTopLaneAnimation();
        CancelAndDisposeBottomLaneAnimation();
    }

    private void CancelAndDisposeTopLaneAnimation()
    {
        _topLaneAnimationCancellation?.Cancel();
        _topLaneAnimationCancellation?.Dispose();
        _topLaneAnimationCancellation = null;
    }

    private void CancelAndDisposeBottomLaneAnimation()
    {
        _bottomLaneAnimationCancellation?.Cancel();
        _bottomLaneAnimationCancellation?.Dispose();
        _bottomLaneAnimationCancellation = null;
    }

    private void RaiseDoubleLineComputedProperties()
    {
        OnPropertyChanged(nameof(IsTopLyricLineVisible));
        OnPropertyChanged(nameof(IsBottomLyricLineVisible));
        OnPropertyChanged(nameof(IsTopPlainTextVisible));
        OnPropertyChanged(nameof(IsBottomPlainTextVisible));
        OnPropertyChanged(nameof(IsTopLegacyWordsVisible));
        OnPropertyChanged(nameof(IsBottomLegacyWordsVisible));
        OnPropertyChanged(nameof(IsTopGradientWordsVisible));
        OnPropertyChanged(nameof(IsBottomGradientWordsVisible));
    }

    private void ApplyLyricStyleSettings(
        bool useCustomMainColor,
        string mainColorHex,
        bool useCustomTranslationColor,
        string translationColorHex,
        bool useCustomFont,
        string fontFamilyName)
    {
        ApplyFontSettings(useCustomFont, fontFamilyName);

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
            ? new FontFamily(fontFamilyName)
            : FontFamily.Default;
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

    private static double ClampFontSize(double fontSize)
    {
        return Math.Clamp(fontSize, MinFontSize, MaxFontSize);
    }

    private double CalculateWindowHeight()
    {
        var lyricContentHeight = IsDoubleLineEnabled
            ? FontSize * 2.65
            : FontSize * 1.45 + (IsDesktopTranslationActuallyVisible ? TranslationFontSize * 1.45 + 8 : 0);

        return Math.Ceiling(Math.Max(
            MinWindowHeight,
            ControlBarReservedHeight + lyricContentHeight + WindowVerticalPadding));
    }
}
