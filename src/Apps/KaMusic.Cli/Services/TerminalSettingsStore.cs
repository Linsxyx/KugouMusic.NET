using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using KaMusic.Cli.Models;

namespace KaMusic.Cli.Services;

internal sealed class TerminalSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "kugou",
        "KgTestTerminalSettings.json");

    private static readonly TerminalSettingsJsonContext JsonContext = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    });

    private readonly string _settingsPath;

    public TerminalSettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? SettingsPath;
    }

    public TerminalAppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new TerminalAppSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize(json, JsonContext.TerminalAppSettings) ?? new TerminalAppSettings();
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
            var dir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(settings, JsonContext.TerminalAppSettings);
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // Terminal settings are non-critical.
        }
    }
}

[JsonSerializable(typeof(TerminalAppSettings))]
internal partial class TerminalSettingsJsonContext : JsonSerializerContext
{
}
