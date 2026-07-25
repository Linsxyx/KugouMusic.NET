using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using KugouAvaloniaPlayer.Models;

namespace KugouAvaloniaPlayer.Services;

public sealed record LyricStylePreferences(
    bool UseCustomMainColor,
    string MainColorHex,
    bool UseCustomTranslationColor,
    string TranslationColorHex,
    bool UseCustomFont,
    string FontFamilyName,
    LyricAlignmentOption Alignment,
    double FontSize);

public sealed record AppBackgroundPreferences(
    bool UseCustomImage,
    string? CustomImagePath,
    double CustomImageOpacity);

public sealed record UiPreferencesSnapshot(
    string GlobalFontFamily,
    AppBackgroundPreferences AppBackground,
    LyricStylePreferences DesktopLyric,
    LyricStylePreferences PlayPageLyric,
    bool DesktopLyricDoubleLineEnabled,
    double NowPlayingBackgroundBlurRadius,
    NowPlayingBackgroundSource NowPlayingBackgroundSource,
    bool UseLightweightNowPlayingLyricScroll);

public interface IUiPreferencesState : INotifyPropertyChanged
{
    UiPreferencesSnapshot Current { get; }

    void RefreshFromSettings();
}

public sealed class UiPreferencesState : IUiPreferencesState
{
    private UiPreferencesSnapshot _current = CreateSnapshot();

    public event PropertyChangedEventHandler? PropertyChanged;

    public UiPreferencesSnapshot Current => _current;

    public void RefreshFromSettings()
    {
        var next = CreateSnapshot();
        if (next == _current)
            return;

        _current = next;
        OnPropertyChanged(nameof(Current));
    }

    private static UiPreferencesSnapshot CreateSnapshot()
    {
        var settings = SettingsManager.Settings;
        return new UiPreferencesSnapshot(
            settings.GlobalFontFamily,
            new AppBackgroundPreferences(
                settings.UseCustomBackgroundImage,
                settings.CustomBackgroundImagePath,
                settings.CustomBackgroundImageOpacity),
            new LyricStylePreferences(
                settings.DesktopLyricUseCustomMainColor,
                settings.DesktopLyricCustomMainColor,
                settings.DesktopLyricUseCustomTranslationColor,
                settings.DesktopLyricCustomTranslationColor,
                settings.DesktopLyricUseCustomFont,
                settings.DesktopLyricCustomFontFamily,
                LyricAlignmentOption.Center,
                settings.DesktopLyricFontSize),
            new LyricStylePreferences(
                settings.PlayPageLyricUseCustomMainColor,
                settings.PlayPageLyricCustomMainColor,
                settings.PlayPageLyricUseCustomTranslationColor,
                settings.PlayPageLyricCustomTranslationColor,
                settings.PlayPageLyricUseCustomFont,
                settings.PlayPageLyricCustomFontFamily,
                settings.PlayPageLyricAlignment,
                settings.PlayPageLyricFontSize),
            settings.DesktopLyricDoubleLineEnabled,
            Math.Clamp(settings.NowPlayingBackgroundBlurRadius, 0.0, 80.0),
            settings.NowPlayingBackgroundSource,
            settings.UseLightweightNowPlayingLyricScroll);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
