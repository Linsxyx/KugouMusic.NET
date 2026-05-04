using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using KuGou.Net.Protocol.Session;

namespace KugouAvaloniaPlayer.Services;

public sealed class KugouSessionPersistence : ISessionPersistence
{
    private const string ProtectedSessionPrefix = "KGSESSION:v1:";

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
            var content = File.ReadAllText(SessionPath);
            var json = UnprotectSessionJson(content);
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
            RestrictDirectoryAccess(dir);

            var json = JsonSerializer.Serialize(session, JsonContext.KgSession);
            var content = ProtectSessionJson(json);
            var tempPath = SessionPath + ".tmp";
            File.WriteAllText(tempPath, content);
            RestrictFileAccess(tempPath);
            File.Move(tempPath, SessionPath, true);
            RestrictFileAccess(SessionPath);
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

    private static string ProtectSessionJson(string json)
    {
        if (!OperatingSystem.IsWindows()) return json;

        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return ProtectedSessionPrefix + Convert.ToBase64String(protectedBytes);
    }

    private static string UnprotectSessionJson(string content)
    {
        if (!content.StartsWith(ProtectedSessionPrefix, StringComparison.Ordinal)) return content;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Protected session file is Windows-only.");

        var protectedBytes = Convert.FromBase64String(content[ProtectedSessionPrefix.Length..]);
        var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static void RestrictDirectoryAccess(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || OperatingSystem.IsWindows()) return;

        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch
        {
            // Best effort: older file systems may not support Unix file modes.
        }
    }

    private static void RestrictFileAccess(string path)
    {
        if (OperatingSystem.IsWindows()) return;

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Best effort: older file systems may not support Unix file modes.
        }
    }
}

[JsonSerializable(typeof(KgSession))]
internal partial class KugouSessionJsonContext : JsonSerializerContext
{
}
