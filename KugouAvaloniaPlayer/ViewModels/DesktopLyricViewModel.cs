using System;
using System.ComponentModel;
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

    [ObservableProperty] private double _fontSize = 30;

    [ObservableProperty] private bool _isLocked;
    [ObservableProperty] private bool _isControlBarExpanded;
    [ObservableProperty] private bool _isControlHotspotHovered;
    [ObservableProperty] private bool _isCollapsedLockIconHovered;
    [ObservableProperty] private bool _enableLegacyWordLyricEffect;
    [ObservableProperty] private bool _isTranslationVisible = true;
    [ObservableProperty] private bool _isDoubleLineEnabled;

    [ObservableProperty] private FontFamily? _lyricFontFamily;
    [ObservableProperty] private IBrush _lyricForeground = DefaultLyricBrush;
    [ObservableProperty] private double _translationFontSize = 18;
    [ObservableProperty] private IBrush _translationLineForeground = DefaultTranslationLineBrush;
    [ObservableProperty] private IBrush _translationWordForeground = DefaultTranslationWordBrush;
    [ObservableProperty] private LyricLineViewModel? _topLyricLine;
    [ObservableProperty] private LyricLineViewModel? _bottomLyricLine;
    [ObservableProperty] private bool _isTopLyricLineCurrent;
    [ObservableProperty] private bool _isBottomLyricLineCurrent;

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
            TopLyricLine = null;
            BottomLyricLine = null;
            IsTopLyricLineCurrent = false;
            IsBottomLyricLineCurrent = false;
            RaiseDoubleLineComputedProperties();
            return;
        }

        var nextLine = Player.NextLyricLine;
        if (currentIndex % 2 == 0)
        {
            TopLyricLine = currentLine;
            BottomLyricLine = nextLine;
            IsTopLyricLineCurrent = true;
            IsBottomLyricLineCurrent = false;
        }
        else
        {
            TopLyricLine = nextLine;
            BottomLyricLine = currentLine;
            IsTopLyricLineCurrent = false;
            IsBottomLyricLineCurrent = true;
        }

        RaiseDoubleLineComputedProperties();
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
            LyricFontFamily = null;
            return;
        }

        LyricFontFamily = IsSystemFontInstalled(fontFamilyName)
            ? new FontFamily(fontFamilyName)
            : null;
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
