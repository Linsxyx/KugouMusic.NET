using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KugouAvaloniaPlayer.Services;

public enum CloseBehavior
{
    Exit,
    MinimizeToTray
}

// 设置数据模型
public class AppSettings
{
    public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.MinimizeToTray;
    public string MusicQuality { get; set; } = "128";
    public List<string> LocalMusicFolders { get; set; } = new();
    public bool AutoCheckUpdate { get; set; } = true;

    public string EQPreset { get; set; } = "原声";

    public bool EnableSurround { get; set; }

    public float[] CustomEqGains { get; set; } = new float[10];

    public bool DesktopLyricUseCustomMainColor { get; set; }
    public string DesktopLyricCustomMainColor { get; set; } = "#FFFFFFFF";
    public bool DesktopLyricUseCustomTranslationColor { get; set; }
    public string DesktopLyricCustomTranslationColor { get; set; } = "#CCFFFFFF";
}

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
