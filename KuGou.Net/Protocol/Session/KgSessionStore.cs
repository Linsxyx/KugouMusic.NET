using System.Text.Json;
using KuGou.Net.util;

namespace KuGou.Net.Protocol.Session;

public static class KgSessionStore
{
    private static readonly string FilePath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "kugou",
            "session.json"
        );


    public static void Save(KgSession session)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(session, AppJsonContext.Default.KgSession);

        File.WriteAllText(FilePath, json);
    }

    public static KgSession? Load()
    {
        if (!File.Exists(FilePath)) return null;

        try
        {
            var json = File.ReadAllText(FilePath);

            return JsonSerializer.Deserialize(json, AppJsonContext.Default.KgSession);
        }
        catch
        {
            return null;
        }
    }

    public static void Clear()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }
}