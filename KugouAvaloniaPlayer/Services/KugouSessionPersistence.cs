using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using KuGou.Net.Protocol.Session;

namespace KugouAvaloniaPlayer.Services;

public sealed class KugouSessionPersistence : ISessionPersistence
{
    private static readonly string SessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "kugou",
        "session.json");
    private static readonly KugouSessionJsonContext JsonContext = new(new JsonSerializerOptions
    {
        WriteIndented = false
    });

    public KgSession? Load()
    {
        if (!File.Exists(SessionPath)) return null;

        try
        {
            var json = File.ReadAllText(SessionPath);
            return JsonSerializer.Deserialize(json, JsonContext.KgSession);
        }
        catch
        {
            return null;
        }
    }

    public void Save(KgSession session)
    {
        try
        {
            var dir = Path.GetDirectoryName(SessionPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(session, JsonContext.KgSession);
            File.WriteAllText(SessionPath, json);
        }
        catch
        {
            // Ignore persistence failures to avoid breaking core playback/login flows.
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(SessionPath)) File.Delete(SessionPath);
        }
        catch
        {
            // Ignore deletion failures.
        }
    }
}

[JsonSerializable(typeof(KgSession))]
internal partial class KugouSessionJsonContext : JsonSerializerContext
{
}
