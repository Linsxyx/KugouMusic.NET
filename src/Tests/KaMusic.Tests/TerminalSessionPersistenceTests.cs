using System.Security.Cryptography;
using KaMusic.Cli.Services;
using KuGou.Net.Protocol.Session;
using Xunit;

// 验证 KgTest 会话持久化在 Windows DPAPI 失败或抛异常时的安全行为（中止写入、保留内存会话与已有文件、警告日志脱敏）。

namespace KaMusic.Tests;

public sealed class TerminalSessionPersistenceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sessionPath;
    private readonly string _legacySessionPath;

    public TerminalSessionPersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"kgtest-session-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _sessionPath = Path.Combine(_tempDir, "KgTestSession.json");
        _legacySessionPath = Path.Combine(_tempDir, "session.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
        }
    }

    // 验证 Windows DPAPI 返回 false 时中止落盘，保留内存会话与磁盘已有文件，并记录不含敏感 Token 的警告。
    [Fact]
    public void Save_OnWindowsWhenDpapiReturnsFalse_AbortsWritePreservesExistingFileAndLogsRedactedWarning()
    {
        const string existingToken = "EXISTING_TOKEN_ALPHA";
        const string newToken = "NEW_SECRET_TOKEN_BETA_999";
        const string newVipToken = "NEW_SECRET_VIP_TOKEN_888";
        const string newT1 = "NEW_SECRET_T1_777";

        var baselinePersistence = CreateWorkingWindowsPersistence(out _);
        baselinePersistence.Save(new KgSession
        {
            UserId = "1001",
            Token = existingToken,
            VipType = "1",
            VipToken = "OLD_VIP",
            T1 = "OLD_T1"
        });

        Assert.True(File.Exists(_sessionPath));
        var baselineDiskContent = File.ReadAllText(_sessionPath);
        Assert.StartsWith(TerminalSessionPersistence.ProtectedSessionPrefix, baselineDiskContent, StringComparison.Ordinal);
        Assert.DoesNotContain(existingToken, baselineDiskContent, StringComparison.Ordinal);

        var warnings = new List<string>();
        var failingPersistence = new TerminalSessionPersistence(
            _sessionPath,
            _legacySessionPath,
            isWindows: static () => true,
            dpapiTransform: (byte[] _, bool _, out byte[] output) =>
            {
                output = [];
                return false;
            },
            warningLogger: warnings.Add);

        var inMemorySession = new KgSession
        {
            UserId = "1002",
            Token = newToken,
            VipType = "6",
            VipToken = newVipToken,
            T1 = newT1
        };

        failingPersistence.Save(inMemorySession);

        // 1. 内存中的会话保持不变
        Assert.Equal("1002", inMemorySession.UserId);
        Assert.Equal(newToken, inMemorySession.Token);
        Assert.Equal(newVipToken, inMemorySession.VipToken);
        Assert.Equal(newT1, inMemorySession.T1);

        // 2. 已有加密文件保持原样，未写入明文或残留临时文件
        Assert.Equal(baselineDiskContent, File.ReadAllText(_sessionPath));
        Assert.False(File.Exists(_sessionPath + ".tmp"));

        var reloaded = baselinePersistence.Load();
        Assert.NotNull(reloaded);
        Assert.Equal(existingToken, reloaded!.Token);

        // 3. 记录了警告且不包含任何敏感凭据
        var warning = Assert.Single(warnings);
        Assert.DoesNotContain(newToken, warning, StringComparison.Ordinal);
        Assert.DoesNotContain(newVipToken, warning, StringComparison.Ordinal);
        Assert.DoesNotContain(newT1, warning, StringComparison.Ordinal);
    }

    // 验证 Windows DPAPI 抛异常时中止落盘，保留内存会话与磁盘已有文件，且即使异常消息含 Token 也不会写入警告。
    [Fact]
    public void Save_OnWindowsWhenDpapiThrowsException_AbortsWritePreservesExistingFileAndLogsRedactedWarning()
    {
        const string existingToken = "EXISTING_TOKEN_GAMMA";
        const string newToken = "NEW_SECRET_TOKEN_DELTA_555";
        const string newVipToken = "NEW_SECRET_VIP_TOKEN_444";
        const string newT1 = "NEW_SECRET_T1_333";

        var baselinePersistence = CreateWorkingWindowsPersistence(out _);
        baselinePersistence.Save(new KgSession
        {
            UserId = "2001",
            Token = existingToken
        });

        var baselineDiskContent = File.ReadAllText(_sessionPath);

        var warnings = new List<string>();
        var throwingPersistence = new TerminalSessionPersistence(
            _sessionPath,
            _legacySessionPath,
            isWindows: static () => true,
            dpapiTransform: (byte[] _, bool _, out byte[] _) =>
                throw new CryptographicException($"DPAPI fault while protecting {newToken}"),
            warningLogger: warnings.Add);

        var inMemorySession = new KgSession
        {
            UserId = "2002",
            Token = newToken,
            VipType = "6",
            VipToken = newVipToken,
            T1 = newT1
        };

        throwingPersistence.Save(inMemorySession);

        // 1. 内存中的会话保持不变
        Assert.Equal("2002", inMemorySession.UserId);
        Assert.Equal(newToken, inMemorySession.Token);
        Assert.Equal(newVipToken, inMemorySession.VipToken);
        Assert.Equal(newT1, inMemorySession.T1);

        // 2. 已有加密文件保持原样，未写入明文或残留临时文件
        Assert.Equal(baselineDiskContent, File.ReadAllText(_sessionPath));
        Assert.False(File.Exists(_sessionPath + ".tmp"));

        var reloaded = baselinePersistence.Load();
        Assert.NotNull(reloaded);
        Assert.Equal(existingToken, reloaded!.Token);

        // 3. 记录了警告且不包含任何敏感凭据
        var warning = Assert.Single(warnings);
        Assert.Contains(nameof(CryptographicException), warning, StringComparison.Ordinal);
        Assert.DoesNotContain(newToken, warning, StringComparison.Ordinal);
        Assert.DoesNotContain(newVipToken, warning, StringComparison.Ordinal);
        Assert.DoesNotContain(newT1, warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_OnWindowsWhenNoExistingFileAndDpapiFails_CreatesNoFileOnDisk()
    {
        const string secretToken = "FIRST_TIME_SECRET_TOKEN_12345";
        var warnings = new List<string>();

        var failingPersistence = new TerminalSessionPersistence(
            _sessionPath,
            _legacySessionPath,
            isWindows: static () => true,
            dpapiTransform: (byte[] _, bool _, out byte[] output) =>
            {
                output = [];
                return false;
            },
            warningLogger: warnings.Add);

        failingPersistence.Save(new KgSession { UserId = "3001", Token = secretToken });

        Assert.False(File.Exists(_sessionPath));
        Assert.False(File.Exists(_sessionPath + ".tmp"));
        var warning = Assert.Single(warnings);
        Assert.DoesNotContain(secretToken, warning, StringComparison.Ordinal);
    }

    private TerminalSessionPersistence CreateWorkingWindowsPersistence(out List<string> warnings)
    {
        var capturedWarnings = new List<string>();
        warnings = capturedWarnings;
        return new TerminalSessionPersistence(
            _sessionPath,
            _legacySessionPath,
            isWindows: static () => true,
            dpapiTransform: static (byte[] input, bool _, out byte[] output) =>
            {
                output = new byte[input.Length];
                for (var i = 0; i < input.Length; i++)
                {
                    output[i] = (byte)(input[i] ^ 0xA5);
                }

                return true;
            },
            warningLogger: capturedWarnings.Add);
    }
}
