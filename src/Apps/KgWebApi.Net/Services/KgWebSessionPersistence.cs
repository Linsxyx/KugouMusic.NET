using System.Text.Json;
using KuGou.Net.Protocol.Session;
using Microsoft.Extensions.Hosting;

namespace KgWebApi.Net.Services;

public sealed class KgWebSessionPersistence(IHostEnvironment hostEnvironment) : ISessionPersistence
{
    private readonly string _sessionPath = Path.Combine(hostEnvironment.ContentRootPath, "App_Data", "session.json");

    public KgSession? Load()
    {
        if (!File.Exists(_sessionPath)) return null;

        try
        {
            var json = File.ReadAllText(_sessionPath);
            return JsonSerializer.Deserialize<KgSession>(json);
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
            var dir = Path.GetDirectoryName(_sessionPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(session);
            File.WriteAllText(_sessionPath, json);
        }
        catch
        {
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_sessionPath)) File.Delete(_sessionPath);
        }
        catch
        {
        }
    }
}
