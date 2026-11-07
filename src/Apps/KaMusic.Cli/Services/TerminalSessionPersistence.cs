using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KuGou.Net.Protocol.Session;

namespace KaMusic.Cli.Services;

// KgTest 独立的登录会话持久化（与 Avalonia 桌面端分开存储）：
// 存储在 ApplicationData/kugou/KgTestSession.json，Windows 下直接调用 crypt32.dll DPAPI，避免跨平台打包时 ProtectedData NuGet 抛 PlatformNotSupportedException。

internal sealed class TerminalSessionPersistence : ISessionPersistence
{
    internal delegate bool DpapiTransformDelegate(byte[] input, bool protect, out byte[] output);

    internal const string ProtectedSessionPrefix = "KGSESSION:v1:";
    private const uint CryptprotectUiForbidden = 0x1;

    private static readonly string DefaultSessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "kugou",
        "KgTestSession.json");

    private static readonly string DefaultLegacySessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "kugou",
        "session.json");

    private readonly string _sessionPath;
    private readonly string _legacySessionPath;
    private readonly Func<bool> _isWindows;
    private readonly DpapiTransformDelegate _dpapiTransform;
    private readonly Action<string> _warningLogger;

    public TerminalSessionPersistence()
        : this(DefaultSessionPath, DefaultLegacySessionPath, OperatingSystem.IsWindows, TryDpapiTransform, DefaultLogWarning)
    {
    }

    internal TerminalSessionPersistence(
        string sessionPath,
        string? legacySessionPath = null,
        Func<bool>? isWindows = null,
        DpapiTransformDelegate? dpapiTransform = null,
        Action<string>? warningLogger = null)
    {
        _sessionPath = sessionPath;
        _legacySessionPath = legacySessionPath ?? DefaultLegacySessionPath;
        _isWindows = isWindows ?? OperatingSystem.IsWindows;
        _dpapiTransform = dpapiTransform ?? TryDpapiTransform;
        _warningLogger = warningLogger ?? DefaultLogWarning;
    }

    internal string? LastWarning { get; private set; }

    public KgSession? Load()
    {
        var path = File.Exists(_sessionPath)
            ? _sessionPath
            : File.Exists(_legacySessionPath)
                ? _legacySessionPath
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
            if (session != null && path == _legacySessionPath)
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
            var json = JsonSerializer.Serialize(session, TerminalSessionJsonContext.Default.KgSession);
            if (!TryProtectSessionJson(json, out var content))
            {
                return;
            }

            var dir = Path.GetDirectoryName(_sessionPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            RestrictDirectoryAccess(dir);

            var tempPath = _sessionPath + ".tmp";
            File.WriteAllText(tempPath, content);
            RestrictFileAccess(tempPath);
            File.Move(tempPath, _sessionPath, true);
            RestrictFileAccess(_sessionPath);
        }
        catch (Exception ex)
        {
            // Keep playback usable even if session persistence fails.
            ReportWarning($"[TerminalSessionPersistence] Failed to persist session file ({ex.GetType().Name}, HResult=0x{ex.HResult:X8}); keeping in-memory session.");
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_sessionPath))
            {
                File.Delete(_sessionPath);
            }
        }
        catch
        {
            // Ignore deletion failures.
        }
    }

    // Windows 下加密会话 JSON；若 DPAPI 返回失败或抛异常则记录脱敏警告并中止落盘，防止明文 Token 写入磁盘。
    private bool TryProtectSessionJson(string json, out string protectedContent)
    {
        if (!_isWindows())
        {
            protectedContent = json;
            return true;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            if (_dpapiTransform(bytes, protect: true, out var encrypted) && encrypted is { Length: > 0 })
            {
                protectedContent = ProtectedSessionPrefix + Convert.ToBase64String(encrypted);
                return true;
            }

            var win32Error = Marshal.GetLastWin32Error();
            ReportWarning($"[TerminalSessionPersistence] Windows DPAPI encryption failed (Win32Error={win32Error}); aborting session save to avoid plaintext token persistence.");
        }
        catch (Exception ex)
        {
            ReportWarning($"[TerminalSessionPersistence] Windows DPAPI encryption threw {ex.GetType().Name} (HResult=0x{ex.HResult:X8}); aborting session save to avoid plaintext token persistence.");
        }

        protectedContent = string.Empty;
        return false;
    }

    private string UnprotectSessionJson(string content)
    {
        if (!content.StartsWith(ProtectedSessionPrefix, StringComparison.Ordinal))
        {
            return content;
        }

        if (!_isWindows())
        {
            throw new PlatformNotSupportedException("Protected session file is Windows-only.");
        }

        var protectedBytes = Convert.FromBase64String(content[ProtectedSessionPrefix.Length..]);
        if (!_dpapiTransform(protectedBytes, protect: false, out var plainBytes))
        {
            throw new InvalidOperationException("Failed to unprotect session data.");
        }

        return Encoding.UTF8.GetString(plainBytes);
    }

    private void ReportWarning(string message)
    {
        LastWarning = message;
        try
        {
            _warningLogger(message);
        }
        catch
        {
            // Never let warning logger throw into session flow.
        }
    }

    private static void DefaultLogWarning(string message)
    {
        Trace.TraceWarning(message);
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
