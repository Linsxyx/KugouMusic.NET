using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace KugouAvaloniaPlayer.Services;

public static class AppFontService
{
    public const string ResourceKey = "AppDefaultFontFamily";
    public const string SystemDefaultOption = "系统默认";

    public static string[] LoadSystemFontFamilies()
    {
        return FontManager.Current.SystemFonts
            .Select(f => f.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static string? NormalizeSystemFontName(string? fontName, string[] availableFonts)
    {
        if (string.IsNullOrWhiteSpace(fontName))
            return null;

        var trimmed = fontName.Trim();
        if (!availableFonts.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            return null;

        return availableFonts.First(name => string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsSystemFontInstalled(string? fontFamilyName)
    {
        if (string.IsNullOrWhiteSpace(fontFamilyName))
            return false;

        foreach (var systemFont in FontManager.Current.SystemFonts)
        {
            if (string.Equals(systemFont.Name, fontFamilyName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static FontFamily ResolveConfiguredGlobalFontFamily()
    {
        var configured = SettingsManager.Settings.GlobalFontFamily;
        return IsSystemFontInstalled(configured)
            ? new FontFamily(configured!)
            : FontFamily.Default;
    }

    public static FontFamily ResolveEffectiveLyricFontFamily(bool useCustomFont, string? customFontFamily)
    {
        if (useCustomFont && IsSystemFontInstalled(customFontFamily))
            return new FontFamily(customFontFamily!);

        return ResolveConfiguredGlobalFontFamily();
    }

    public static string FormatGlobalFontSelection(string[] availableFonts)
    {
        return NormalizeSystemFontName(SettingsManager.Settings.GlobalFontFamily, availableFonts) ?? SystemDefaultOption;
    }

    public static string NormalizeGlobalFontSelection(string? selectedValue, string[] availableFonts)
    {
        if (string.IsNullOrWhiteSpace(selectedValue) ||
            string.Equals(selectedValue, SystemDefaultOption, StringComparison.Ordinal))
            return string.Empty;

        return NormalizeSystemFontName(selectedValue, availableFonts) ?? string.Empty;
    }

    public static void ApplyGlobalFont(Application? application = null)
    {
        var target = application ?? Application.Current;
        if (target == null)
            return;

        target.Resources[ResourceKey] = ResolveConfiguredGlobalFontFamily();
    }
}
