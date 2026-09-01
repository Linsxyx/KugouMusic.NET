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
[JsonSerializable(typeof(DesktopLyricLayoutMode))]
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
[JsonSerializable(typeof(AdvancedAudioEffectsSettings))]
[JsonSerializable(typeof(AudioEffectsPreset))]
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
        Settings.VerticalDesktopLyricWindowPosition ??= new DesktopLyricWindowPositionSettings();
        Settings.AppTheme = NormalizeAppTheme(Settings.AppTheme);
        if (!Enum.IsDefined(Settings.MainWindowState.State))
            Settings.MainWindowState.State = SavedMainWindowState.Normal;
        if (!Enum.IsDefined(Settings.PlaybackMode))
            Settings.PlaybackMode = PlayMode.Normal;
        if (!Enum.IsDefined(Settings.UserPlaylistSongSortMode))
            Settings.UserPlaylistSongSortMode = PlaylistSongSortMode.Default;
        if (!Enum.IsDefined(Settings.LocalPlaylistSongSortMode))
            Settings.LocalPlaylistSongSortMode = PlaylistSongSortMode.Default;
        if (!Settings.HasDesktopLyricAlignmentPreference || !Enum.IsDefined(Settings.DesktopLyricAlignment))
            Settings.DesktopLyricAlignment = LyricAlignmentOption.Center;
        if (!Enum.IsDefined(Settings.DesktopLyricLayoutMode))
            Settings.DesktopLyricLayoutMode = DesktopLyricLayoutMode.Horizontal;
        Settings.CustomBackgroundImagePath = string.IsNullOrWhiteSpace(Settings.CustomBackgroundImagePath)
            ? null
            : Settings.CustomBackgroundImagePath;
        Settings.CustomBackgroundImageOpacity = Math.Clamp(Settings.CustomBackgroundImageOpacity, 0.1, 1.0);
        Settings.MusicVolume = Math.Clamp(Settings.MusicVolume, 0f, 1f);
        Settings.PlaybackSpeed = Math.Clamp(Settings.PlaybackSpeed, 0.5f, 2.0f);
        if (!SimpleAudioPlayer.IsOutputDeviceAvailable(Settings.AudioOutputDeviceId))
            Settings.AudioOutputDeviceId = AppSettings.SystemDefaultAudioOutputDeviceId;
        Settings.CustomEqGains = NormalizeCustomEqGains(Settings.CustomEqGains);
        Settings.AdvancedAudioEffects ??= new AdvancedAudioEffectsSettings();
        var fx = Settings.AdvancedAudioEffects;
        fx.StereoWidth = Math.Clamp(fx.StereoWidth, 0f, 1f);
        fx.StereoOutputGain = Math.Clamp(fx.StereoOutputGain, 0f, 2f);
        fx.ReverbAmount = Math.Clamp(fx.ReverbAmount, 0f, 1f);
        fx.ReverbTimeMs = Math.Clamp(fx.ReverbTimeMs, 100f, 3000f);
        fx.EchoMix = Math.Clamp(fx.EchoMix, 0f, 1f);
        fx.EchoFeedback = Math.Clamp(fx.EchoFeedback, 0f, 0.95f);
        fx.EchoDelayMs = Math.Clamp(fx.EchoDelayMs, 1f, 1000f);
        fx.ChorusMix = Math.Clamp(fx.ChorusMix, 0f, 1f);
        fx.ChorusDepth = Math.Clamp(fx.ChorusDepth, 0f, 100f);
        fx.ChorusRate = Math.Clamp(fx.ChorusRate, 0.01f, 10f);
        fx.CompressorThreshold = Math.Clamp(fx.CompressorThreshold, -60f, 0f);
        fx.CompressorRatio = Math.Clamp(fx.CompressorRatio, 1f, 20f);
        fx.CompressorAttackMs = Math.Clamp(fx.CompressorAttackMs, 0.1f, 200f);
        fx.CompressorReleaseMs = Math.Clamp(fx.CompressorReleaseMs, 1f, 1000f);
        fx.DistortionDrive = Math.Clamp(fx.DistortionDrive, 0f, 1f);
        fx.DistortionMix = Math.Clamp(fx.DistortionMix, 0f, 1f);
        fx.BqfCenterHz = Math.Clamp(fx.BqfCenterHz, 20f, 20000f);
        fx.BqfGainDb = Math.Clamp(fx.BqfGainDb, -24f, 24f);
        fx.BqfQ = Math.Clamp(fx.BqfQ, 0.1f, 10f);
        fx.FlangerMix = Math.Clamp(fx.FlangerMix, 0f, 1f); fx.FlangerDepth = Math.Clamp(fx.FlangerDepth, 0f, 100f); fx.FlangerRate = Math.Clamp(fx.FlangerRate, 0.01f, 10f);
        fx.PhaserMix = Math.Clamp(fx.PhaserMix, 0f, 1f); fx.PhaserRate = Math.Clamp(fx.PhaserRate, 0.01f, 10f); fx.PhaserRange = Math.Clamp(fx.PhaserRange, 0f, 10f); fx.PhaserFrequency = Math.Clamp(fx.PhaserFrequency, 20f, 20000f);
        fx.GargleRateHz = Math.Clamp(fx.GargleRateHz, 1, 2000);
        fx.AutoWahMix = Math.Clamp(fx.AutoWahMix, 0f, 1f); fx.AutoWahRate = Math.Clamp(fx.AutoWahRate, 0.01f, 10f); fx.AutoWahRange = Math.Clamp(fx.AutoWahRange, 0f, 10f); fx.AutoWahFrequency = Math.Clamp(fx.AutoWahFrequency, 20f, 20000f);
        fx.DampTarget = Math.Clamp(fx.DampTarget, 0f, 1f); fx.DampQuiet = Math.Clamp(fx.DampQuiet, 0f, 1f); fx.DampRate = Math.Clamp(fx.DampRate, 0f, 10f); fx.DampGain = Math.Clamp(fx.DampGain, 0f, 2f); fx.DampDelay = Math.Clamp(fx.DampDelay, 0f, 1000f);
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
