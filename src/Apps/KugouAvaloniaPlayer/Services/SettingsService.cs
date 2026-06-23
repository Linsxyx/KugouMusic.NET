using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using KugouAvaloniaPlayer.Models;
using Serilog;
using SimpleAudio;

namespace KugouAvaloniaPlayer.Services;

[JsonSerializable(typeof(GlobalShortcutSettings))]
[JsonSerializable(typeof(PlayMode))]
[JsonSerializable(typeof(LyricAlignmentOption))]
[JsonSerializable(typeof(NowPlayingLyricDisplayMode))]
[JsonSerializable(typeof(NowPlayingBackgroundSource))]
[JsonSerializable(typeof(SavedMainWindowState))]
[JsonSerializable(typeof(MainWindowStateSettings))]
[JsonSerializable(typeof(DesktopLyricWindowPositionSettings))]
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
        catch (Exception ex)
        {
            Settings = new AppSettings();
            Log.Warning(ex, "加载应用设置失败，已使用默认设置。");
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
        catch (Exception ex)
        {
            Log.Warning(ex, "保存应用设置失败。");
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
        catch (Exception ex)
        {
            Log.Warning(ex, "重置应用设置失败。");
        }
    }

    private static void NormalizeSettings()
    {
        Settings.LocalMusicFolders ??= new List<string>();
        Settings.LocalPlaylistMetas ??= new Dictionary<string, LocalPlaylistMeta>();
        Settings.JellyfinServers ??= new Dictionary<string, JellyfinServerSettings>();
        Settings.GlobalShortcuts ??= new GlobalShortcutSettings();
        Settings.MainWindowState ??= new MainWindowStateSettings();
        Settings.DesktopLyricWindowPosition ??= new DesktopLyricWindowPositionSettings();
        Settings.AppTheme = NormalizeAppTheme(Settings.AppTheme);
        if (!Enum.IsDefined(Settings.MainWindowState.State))
            Settings.MainWindowState.State = SavedMainWindowState.Normal;
        if (!Enum.IsDefined(Settings.PlaybackMode))
            Settings.PlaybackMode = PlayMode.Normal;
        Settings.CustomBackgroundImagePath = string.IsNullOrWhiteSpace(Settings.CustomBackgroundImagePath)
            ? null
            : Settings.CustomBackgroundImagePath;
        Settings.CustomBackgroundImageOpacity = Math.Clamp(Settings.CustomBackgroundImageOpacity, 0.1, 1.0);
        Settings.MusicVolume = Math.Clamp(Settings.MusicVolume, 0f, 1f);
        if (!SimpleAudioPlayer.IsOutputDeviceAvailable(Settings.AudioOutputDeviceId))
            Settings.AudioOutputDeviceId = AppSettings.SystemDefaultAudioOutputDeviceId;
        Settings.CustomEqGains = NormalizeCustomEqGains(Settings.CustomEqGains);
    }

    private static float[] NormalizeCustomEqGains(float[]? gains)
    {
        if (gains is { Length: AppSettings.CustomEqBandCount })
            return gains;

        var normalized = new float[AppSettings.CustomEqBandCount];
        if (gains == null)
            return normalized;

        Array.Copy(gains, normalized, Math.Min(gains.Length, normalized.Length));
        return normalized;
    }
}
