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
[JsonSerializable(typeof(NowPlayingBackgroundSource))]
[JsonSerializable(typeof(LocalPlaylistMeta))]
[JsonSerializable(typeof(JellyfinServerSettings))]
[JsonSerializable(typeof(Dictionary<string, LocalPlaylistMeta>))]
[JsonSerializable(typeof(Dictionary<string, JellyfinServerSettings>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}

// 设置管理器
public static class SettingsManager
{
    private const string StoreScope = "settings";
    private const string StoreKey = "app";

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
            var json = AppSqliteStore.LoadValue(StoreScope, StoreKey);
            if (string.IsNullOrWhiteSpace(json) && File.Exists(SettingsPath))
            {
                json = File.ReadAllText(SettingsPath);
                AppSqliteStore.SaveValue(StoreScope, StoreKey, json);
                AppSqliteStore.DeleteFileIfExists(SettingsPath);
            }

            if (string.IsNullOrWhiteSpace(json))
                return;

            Settings = JsonSerializer.Deserialize(json, JsonContext.AppSettings) ?? new AppSettings();
            NormalizeSettings();
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
            var json = JsonSerializer.Serialize(Settings, JsonContext.AppSettings);
            AppSqliteStore.SaveValue(StoreScope, StoreKey, json);
            AppSqliteStore.DeleteFileIfExists(SettingsPath);
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
            var jellyfinServers = Settings.JellyfinServers;
            var lastJellyfinServerFingerprint = Settings.LastJellyfinServerFingerprint;
            Settings = new AppSettings
            {
                LocalMusicFolders = localFolders,
                LocalPlaylistMetas = localMetas,
                JellyfinServers = jellyfinServers,
                LastJellyfinServerFingerprint = lastJellyfinServerFingerprint
            };
            Save();
        }
        catch (Exception)
        {
        }
    }

    private static void NormalizeSettings()
    {
        Settings.LocalMusicFolders ??= new List<string>();
        Settings.LocalPlaylistMetas ??= new Dictionary<string, LocalPlaylistMeta>();
        Settings.JellyfinServers ??= new Dictionary<string, JellyfinServerSettings>();
        Settings.GlobalShortcuts ??= new GlobalShortcutSettings();
        Settings.AppTheme = NormalizeAppTheme(Settings.AppTheme);
        Settings.CustomBackgroundImagePath = string.IsNullOrWhiteSpace(Settings.CustomBackgroundImagePath)
            ? null
            : Settings.CustomBackgroundImagePath;
        Settings.CustomBackgroundImageOpacity = Math.Clamp(Settings.CustomBackgroundImageOpacity, 0.1, 1.0);
        Settings.MusicVolume = Math.Clamp(Settings.MusicVolume, 0f, 1f);
    }
}
