using System;
using System.ComponentModel;
using Avalonia.Layout;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class DesktopLyricViewModel : ViewModelBase, IDisposable
{
    private const double MinFontSize = 18;
    private const double MaxFontSize = 50;
    private const double FontSizeStep = 2;
    private const double HorizontalControlBarReservedHeight = 64;
    private const double HorizontalMinWindowHeight = 140;
    private const double HorizontalWindowVerticalPadding = 24;
    public const double VerticalBaseWindowWidth = 320;

    private static readonly IBrush DefaultLyricBrush = new SolidColorBrush(Colors.White);
    private static readonly IBrush DefaultTranslationLineBrush = new SolidColorBrush(Color.Parse("#CCFFFFFF"));
    private static readonly IBrush DefaultTranslationWordBrush = new SolidColorBrush(Colors.White);

    private readonly IUiPreferencesState _uiPreferencesState;
    private double _verticalContentHeight = 560;

    [ObservableProperty] public partial double FontSize { get; set; } = 30;
    [ObservableProperty] public partial bool IsLocked { get; set; }
    [ObservableProperty] public partial bool IsControlBarExpanded { get; set; }
    [ObservableProperty] public partial bool IsControlHotspotHovered { get; set; }
    [ObservableProperty] public partial bool IsCollapsedLockIconHovered { get; set; }
    [ObservableProperty] public partial bool IsTranslationVisible { get; set; } = true;
    [ObservableProperty] public partial bool IsDoubleLineEnabled { get; set; }
    [ObservableProperty] public partial FontFamily LyricFontFamily { get; set; } = FontFamily.Default;
    [ObservableProperty] public partial IBrush LyricForeground { get; set; } = DefaultLyricBrush;
    [ObservableProperty] public partial HorizontalAlignment LyricHorizontalAlignment { get; set; } = HorizontalAlignment.Center;
    [ObservableProperty] public partial TextAlignment LyricTextAlignment { get; set; } = TextAlignment.Center;
    [ObservableProperty] public partial double TranslationFontSize { get; set; } = 18;
    [ObservableProperty] public partial IBrush TranslationLineForeground { get; set; } = DefaultTranslationLineBrush;
    [ObservableProperty] public partial IBrush TranslationWordForeground { get; set; } = DefaultTranslationWordBrush;
    [ObservableProperty] public partial DesktopLyricLayoutMode LayoutMode { get; set; }
    [ObservableProperty] public partial IDesktopLyricLayoutViewModel? ActiveLayout { get; set; }
    [ObservableProperty] public partial double VerticalDesiredWidth { get; set; } = VerticalBaseWindowWidth;

    public DesktopLyricViewModel(
        PlayerViewModel player,
        IUiPreferencesState uiPreferencesState,
        bool canMousePassthrough,
        bool usesSeparateLockOverlay)
    {
        Player = player;
        _uiPreferencesState = uiPreferencesState;
        CanMousePassthrough = canMousePassthrough;
        UsesSeparateLockOverlay = canMousePassthrough && usesSeparateLockOverlay;
        FontSize = ClampFontSize(SettingsManager.Settings.DesktopLyricFontSize);
        IsTranslationVisible = SettingsManager.Settings.DesktopLyricShowTranslation;
        ApplyUiPreferences(_uiPreferencesState.Current);
        _uiPreferencesState.PropertyChanged += OnUiPreferencesChanged;
    }

    public bool CanMousePassthrough { get; }
    public bool UsesSeparateLockOverlay { get; }
    public PlayerViewModel Player { get; }
    public string FontSizeDisplay => $"{Math.Round(FontSize):0}pt";
    public double WindowHeight => CalculateHorizontalWindowHeight();
    public bool IsUnlockedInteractionEnabled => !IsLocked;
    public bool IsCollapsedLockIconVisible => CanMousePassthrough && IsLocked;
    public bool IsEmbeddedCollapsedLockIconVisible => IsCollapsedLockIconVisible && !UsesSeparateLockOverlay;
    public bool IsSingleLineMode => !IsDoubleLineEnabled;
    public bool IsDesktopTranslationActuallyVisible => IsTranslationVisible && !IsDoubleLineEnabled;

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
        ResetVerticalDesiredWidth();
        OnPropertyChanged(nameof(FontSizeDisplay));
        OnPropertyChanged(nameof(WindowHeight));
    }

    partial void OnIsTranslationVisibleChanged(bool value)
    {
        SettingsManager.Settings.DesktopLyricShowTranslation = value;
        SettingsManager.Save();
        ResetVerticalDesiredWidth();
        OnPropertyChanged(nameof(IsDesktopTranslationActuallyVisible));
        OnPropertyChanged(nameof(WindowHeight));
        ActiveLayout?.RefreshFromPlayer();
    }

    partial void OnIsDoubleLineEnabledChanged(bool value)
    {
        SettingsManager.Settings.DesktopLyricDoubleLineEnabled = value;
        SettingsManager.Save();
        ResetVerticalDesiredWidth();
        OnPropertyChanged(nameof(IsSingleLineMode));
        OnPropertyChanged(nameof(IsDesktopTranslationActuallyVisible));
        OnPropertyChanged(nameof(WindowHeight));
        ActiveLayout?.RefreshFromPlayer();
    }

    partial void OnIsLockedChanged(bool value)
    {
        IsControlBarExpanded = false;
        IsControlHotspotHovered = false;
        if (!value)
            IsCollapsedLockIconHovered = false;

        OnPropertyChanged(nameof(IsUnlockedInteractionEnabled));
        OnPropertyChanged(nameof(IsCollapsedLockIconVisible));
        OnPropertyChanged(nameof(IsEmbeddedCollapsedLockIconVisible));
    }

    partial void OnIsControlHotspotHoveredChanged(bool value)
    {
        if (!IsLocked)
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

    public void ReportVerticalDesiredWidth(double width)
    {
        var normalized = Math.Max(VerticalBaseWindowWidth, Math.Ceiling(width));
        if (normalized > VerticalDesiredWidth)
            VerticalDesiredWidth = normalized;
    }

    public void ResetVerticalDesiredWidth()
    {
        VerticalDesiredWidth = VerticalBaseWindowWidth;
    }

    public void ConfigureVerticalContentHeight(double height)
    {
        var normalized = Math.Max(120d, height);
        if (Math.Abs(_verticalContentHeight - normalized) < 1d)
            return;

        _verticalContentHeight = normalized;
        ResetVerticalDesiredWidth();
        ActiveLayout?.RefreshFromPlayer();
    }

    public double VerticalContentHeight => _verticalContentHeight;

    public void NotifyHorizontalContentChanged()
    {
        OnPropertyChanged(nameof(WindowHeight));
    }

    public void Dispose()
    {
        var activeLayout = ActiveLayout;
        ActiveLayout = null;
        activeLayout?.Dispose();
        _uiPreferencesState.PropertyChanged -= OnUiPreferencesChanged;
    }

    private void OnUiPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(IUiPreferencesState.Current))
            ApplyUiPreferences(_uiPreferencesState.Current);
    }

    private void ApplyUiPreferences(UiPreferencesSnapshot preferences)
    {
        var lyric = preferences.DesktopLyric;
        IsDoubleLineEnabled = preferences.DesktopLyricDoubleLineEnabled;
        ApplyLyricStyleSettings(
            lyric.UseCustomMainColor,
            lyric.MainColorHex,
            lyric.UseCustomTranslationColor,
            lyric.TranslationColorHex,
            lyric.UseCustomFont,
            lyric.FontFamilyName,
            lyric.Alignment);
        ActivateLayout(preferences.DesktopLyricLayoutMode);
    }

    private void ActivateLayout(DesktopLyricLayoutMode mode)
    {
        if (ActiveLayout?.Mode == mode)
            return;

        var previousLayout = ActiveLayout;
        ActiveLayout = null;
        previousLayout?.Dispose();
        LayoutMode = mode;
        ResetVerticalDesiredWidth();
        ActiveLayout = mode == DesktopLyricLayoutMode.Vertical
            ? new VerticalDesktopLyricLayoutViewModel(this)
            : new HorizontalDesktopLyricLayoutViewModel(this);
        OnPropertyChanged(nameof(WindowHeight));
    }

    private void ApplyLyricStyleSettings(
        bool useCustomMainColor,
        string mainColorHex,
        bool useCustomTranslationColor,
        string translationColorHex,
        bool useCustomFont,
        string fontFamilyName,
        LyricAlignmentOption alignment)
    {
        LyricFontFamily = AppFontService.ResolveEffectiveLyricFontFamily(useCustomFont, fontFamilyName);
        (LyricHorizontalAlignment, LyricTextAlignment) = alignment switch
        {
            LyricAlignmentOption.Left => (HorizontalAlignment.Left, TextAlignment.Left),
            LyricAlignmentOption.Right => (HorizontalAlignment.Right, TextAlignment.Right),
            _ => (HorizontalAlignment.Center, TextAlignment.Center)
        };

        LyricForeground = useCustomMainColor
            ? new SolidColorBrush(ParseColorOrDefault(mainColorHex, Colors.White))
            : DefaultLyricBrush;
        if (useCustomTranslationColor)
        {
            var color = new SolidColorBrush(ParseColorOrDefault(translationColorHex, Color.Parse("#CCFFFFFF")));
            TranslationLineForeground = color;
            TranslationWordForeground = color;
        }
        else
        {
            TranslationLineForeground = DefaultTranslationLineBrush;
            TranslationWordForeground = DefaultTranslationWordBrush;
        }
    }

    private static Color ParseColorOrDefault(string? colorText, Color fallback)
    {
        return Color.TryParse(colorText, out var parsed) ? parsed : fallback;
    }

    private static double ClampFontSize(double fontSize)
    {
        return Math.Clamp(fontSize, MinFontSize, MaxFontSize);
    }

    private double CalculateHorizontalWindowHeight()
    {
        var lyricContentHeight = IsDoubleLineEnabled
            ? FontSize * 2.65
            : FontSize * 1.45 + (IsDesktopTranslationActuallyVisible ? TranslationFontSize * 1.45 + 8 : 0);
        return Math.Ceiling(Math.Max(
            HorizontalMinWindowHeight,
            HorizontalControlBarReservedHeight + lyricContentHeight + HorizontalWindowVerticalPadding));
    }
}
