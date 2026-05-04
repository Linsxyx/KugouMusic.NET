using System.Text.Encodings.Web;
using System.Text.Json;
using KgTest.Models;

namespace KgTest.Services;

internal sealed class TerminalSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "kugou",
        "KgTestTerminalSettings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    public TerminalAppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new TerminalAppSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<TerminalAppSettings>(json, JsonOptions) ?? new TerminalAppSettings();
            if (settings.CustomEqGains.Length != 10)
            {
                settings.CustomEqGains = new float[10];
            }

            settings.Volume = Math.Clamp(settings.Volume, 0f, 1f);
            return settings;
        }
        catch
        {
            return new TerminalAppSettings();
        }
    }

    public void Save(TerminalAppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Terminal settings are non-critical.
        }
    }
}
