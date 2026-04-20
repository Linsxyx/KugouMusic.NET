using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using KugouAvaloniaPlayer.Models;

namespace KugouAvaloniaPlayer.Services;

[JsonSerializable(typeof(GlobalShortcutSettings))]
[JsonSerializable(typeof(LyricAlignmentOption))]
[JsonSerializable(typeof(NowPlayingLyricDisplayMode))]
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
}