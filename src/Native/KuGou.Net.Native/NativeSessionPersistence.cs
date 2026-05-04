using System.Text.Json;
using KuGou.Net.Protocol.Session;

namespace KuGou.Net.Native;

public sealed class NativeSessionPersistence : ISessionPersistence
{
    private static readonly string SessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "kugou",
        "session.json");

    public KgSession? Load()
    {
        if (!File.Exists(SessionPath)) return null;

        try
        {
            var json = File.ReadAllText(SessionPath);
            return JsonSerializer.Deserialize(json, NativeJsonContext.Default.KgSession);
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

            var json = JsonSerializer.Serialize(session, NativeJsonContext.Default.KgSession);
            File.WriteAllText(SessionPath, json);
        }
        catch
        {
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
        }
    }
}
