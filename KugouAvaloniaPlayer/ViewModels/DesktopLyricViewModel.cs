using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class DesktopLyricViewModel : ViewModelBase
{
    private const double MinFontSize = 18;
    private const double MaxFontSize = 50;
    private const double FontSizeStep = 2;

    private static readonly IBrush DefaultLyricBrush = new SolidColorBrush(Colors.White);
    private static readonly IBrush DefaultTranslationLineBrush = new SolidColorBrush(Color.Parse("#CCFFFFFF"));
    private static readonly IBrush DefaultTranslationWordBrush = new SolidColorBrush(Colors.White);

    [ObservableProperty] private double _fontSize = 30;

    [ObservableProperty] private bool _isLocked;

    [ObservableProperty] private FontFamily? _lyricFontFamily;
    [ObservableProperty] private IBrush _lyricForeground = DefaultLyricBrush;
    [ObservableProperty] private double _translationFontSize = 18;
    [ObservableProperty] private IBrush _translationLineForeground = DefaultTranslationLineBrush;
    [ObservableProperty] private IBrush _translationWordForeground = DefaultTranslationWordBrush;

    public DesktopLyricViewModel(PlayerViewModel player, bool canMousePassthrough)
    {
        Player = player;
        CanMousePassthrough = canMousePassthrough;
        FontSize = ClampFontSize(SettingsManager.Settings.DesktopLyricFontSize);
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
        });
    }

    public bool CanMousePassthrough { get; }

    public PlayerViewModel Player { get; }

    public string FontSizeDisplay => $"{Math.Round(FontSize):0}pt";

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
}