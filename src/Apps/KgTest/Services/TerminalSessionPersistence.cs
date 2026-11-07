using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KuGou.Net.Protocol.Session;

namespace KgTest.Services;

// KgTest 独立的登录会话持久化（与 Avalonia 桌面端分开存储）：
// 存储在 ApplicationData/kugou/KgTestSession.json，Windows 下直接调用 crypt32.dll DPAPI，避免跨平台打包时 ProtectedData NuGet 抛 PlatformNotSupportedException。

internal sealed class TerminalSessionPersistence : ISessionPersistence
{
    private const string ProtectedSessionPrefix = "KGSESSION:v1:";
    private const uint CryptprotectUiForbidden = 0x1;

    private static readonly string SessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "kugou",
        "KgTestSession.json");

    private static readonly string LegacySessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "kugou",
        "session.json");

    public KgSession? Load()
    {
        var path = File.Exists(SessionPath)
            ? SessionPath
            : File.Exists(LegacySessionPath)
                ? LegacySessionPath
                : null;
        if (path == null)
        {
            return null;
        }

        try
        {
            var content = File.ReadAllText(path);
            var json = UnprotectSessionJson(content);
            var session = JsonSerializer.Deserialize(json, TerminalSessionJsonContext.Default.KgSession);
            if (session != null && path == LegacySessionPath)
            {
                Save(session);
            }

            return session;
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
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            RestrictDirectoryAccess(dir);

            var json = JsonSerializer.Serialize(session, TerminalSessionJsonContext.Default.KgSession);
            var content = ProtectSessionJson(json);
            var tempPath = SessionPath + ".tmp";
            File.WriteAllText(tempPath, content);
            RestrictFileAccess(tempPath);
            File.Move(tempPath, SessionPath, true);
            RestrictFileAccess(SessionPath);
        }
        catch
        {
            // Keep playback usable even if session persistence fails.
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(SessionPath))
            {
                File.Delete(SessionPath);
            }
        }
        catch
        {
            // Ignore deletion failures.
        }
    }

    private static string ProtectSessionJson(string json)
    {
        if (!OperatingSystem.IsWindows())
        {
            return json;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            if (TryDpapiTransform(bytes, protect: true, out var encrypted))
            {
                return ProtectedSessionPrefix + Convert.ToBase64String(encrypted);
            }
        }
        catch
        {
        }

        return json;
    }

    private static string UnprotectSessionJson(string content)
    {
        if (!content.StartsWith(ProtectedSessionPrefix, StringComparison.Ordinal))
        {
            return content;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Protected session file is Windows-only.");
        }

        var protectedBytes = Convert.FromBase64String(content[ProtectedSessionPrefix.Length..]);
        if (!TryDpapiTransform(protectedBytes, protect: false, out var plainBytes))
        {
            throw new InvalidOperationException("Failed to unprotect session data.");
        }

        return Encoding.UTF8.GetString(plainBytes);
    }

    private static bool TryDpapiTransform(byte[] input, bool protect, out byte[] output)
    {
        output = [];
        var inHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
        var outBlob = new DataBlob();
        try
        {
            var inBlob = new DataBlob
            {
                cbData = input.Length,
                pbData = inHandle.AddrOfPinnedObject()
            };

            var ok = protect
                ? CryptProtectData(ref inBlob, null, nint.Zero, nint.Zero, nint.Zero, CryptprotectUiForbidden, out outBlob)
                : CryptUnprotectData(ref inBlob, nint.Zero, nint.Zero, nint.Zero, nint.Zero, CryptprotectUiForbidden, out outBlob);
            if (!ok || outBlob.pbData == nint.Zero)
            {
                return false;
            }

            output = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, output, 0, outBlob.cbData);
            return true;
        }
        finally
        {
            inHandle.Free();
            if (outBlob.pbData != nint.Zero)
            {
                LocalFree(outBlob.pbData);
            }
        }
    }

    private static void RestrictDirectoryAccess(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch
        {
        }
    }

    private static void RestrictFileAccess(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public nint pbData;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn,
        string? szDataDescr,
        nint pOptionalEntropy,
        nint pvReserved,
        nint pPromptStruct,
        uint dwFlags,
        out DataBlob pDataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        nint ppszDataDescr,
        nint pOptionalEntropy,
        nint pvReserved,
        nint pPromptStruct,
        uint dwFlags,
        out DataBlob pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint LocalFree(nint hMem);
}

[JsonSerializable(typeof(KgSession))]
internal partial class TerminalSessionJsonContext : JsonSerializerContext
{
}
