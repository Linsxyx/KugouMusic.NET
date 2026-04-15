using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using Avalonia.Media;
using System;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class DesktopLyricViewModel : ViewModelBase
{
    private static readonly IBrush DefaultLyricBrush = new SolidColorBrush(Colors.White);
    private static readonly IBrush DefaultTranslationLineBrush = new SolidColorBrush(Color.Parse("#CCFFFFFF"));
    private static readonly IBrush DefaultTranslationWordBrush = new SolidColorBrush(Colors.White);

    public DesktopLyricViewModel(PlayerViewModel player, bool canMousePassthrough)
    {
        Player = player;
        CanMousePassthrough = canMousePassthrough;
        ApplyLyricStyleSettings(
            SettingsManager.Settings.DesktopLyricUseCustomMainColor,
            SettingsManager.Settings.DesktopLyricCustomMainColor,
            SettingsManager.Settings.DesktopLyricUseCustomTranslationColor,
            SettingsManager.Settings.DesktopLyricCustomTranslationColor,
            SettingsManager.Settings.DesktopLyricUseCustomFont,
            SettingsManager.Settings.DesktopLyricCustomFontFamily);

        WeakReferenceMessenger.Default.Register<DesktopLyricColorSettingsChangedMessage>(this, (_, message) =>
        {
            ApplyLyricStyleSettings(
                message.UseCustomMainColor,
                message.MainColorHex,
                message.UseCustomTranslationColor,
                message.TranslationColorHex,
                message.UseCustomFont,
                message.FontFamilyName);
        });
    }

    [ObservableProperty] private double _fontSize = 30;

    [ObservableProperty] private bool _isLocked;

    [ObservableProperty] private FontFamily? _lyricFontFamily;
    [ObservableProperty] private IBrush _lyricForeground = DefaultLyricBrush;
    [ObservableProperty] private IBrush _translationLineForeground = DefaultTranslationLineBrush;
    [ObservableProperty] private IBrush _translationWordForeground = DefaultTranslationWordBrush;

    public bool CanMousePassthrough { get; }

    public PlayerViewModel Player { get; }

    [RelayCommand]
    private void ToggleLock()
    {
        IsLocked = !IsLocked;
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
        {
            if (string.Equals(systemFont.Name, fontFamilyName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static Color ParseColorOrDefault(string? colorText, Color fallback)
    {
        return Color.TryParse(colorText, out var parsed) ? parsed : fallback;
    }
}
