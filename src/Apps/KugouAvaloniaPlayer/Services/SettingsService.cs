using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using KugouAvaloniaPlayer.Models;

namespace KugouAvaloniaPlayer.Services;

[JsonSerializable(typeof(GlobalShortcutSettings))]
[JsonSerializable(typeof(LyricAlignmentOption))]
[JsonSerializable(typeof(NowPlayingLyricDisplayMode))]
[JsonSerializable(typeof(LocalPlaylistMeta))]
[JsonSerializable(typeof(Dictionary<string, LocalPlaylistMeta>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}

// 设置管理器
public static class SettingsManager
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "kugou",
        "AvaloniaPlayerSettings.json");

    private static readonly AppSettingsJsonContext JsonContext = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });

    public static AppSettings Settings { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Settings = JsonSerializer.Deserialize(json, JsonContext.AppSettings) ?? new AppSettings();
                Settings.LocalMusicFolders ??= new List<string>();
                Settings.LocalPlaylistMetas ??= new Dictionary<string, LocalPlaylistMeta>();
                Settings.GlobalShortcuts ??= new GlobalShortcutSettings();
                Settings.AppTheme = NormalizeAppTheme(Settings.AppTheme);
                Settings.CustomBackgroundImageOpacity = Math.Clamp(Settings.CustomBackgroundImageOpacity, 0.1, 1.0);
            }
        }
        catch (Exception)
        {
            Settings = new AppSettings();
            //Console.WriteLine(ex.Message);
        }
    }

    public static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

            var json = JsonSerializer.Serialize(Settings, JsonContext.AppSettings);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception)
        {
            //Console.WriteLine($"[SettingsManager] 保存配置文件失败: {ex.Message}");
        }
    }

    private static string NormalizeAppTheme(string? theme)
    {
        return theme switch
        {
            AppSettings.ThemeDark => AppSettings.ThemeDark,
            AppSettings.ThemeLight => AppSettings.ThemeLight,
            _ => AppSettings.ThemeDefault
        };
    }

    public static void ResetSettings()
    {
        try
        {
            var localFolders = Settings.LocalMusicFolders;
            var localMetas = Settings.LocalPlaylistMetas;
            Settings = new AppSettings
            {
                LocalMusicFolders = localFolders,
                LocalPlaylistMetas = localMetas
            };
            Save();
        }
        catch (Exception)
        {
        }
    }
}
