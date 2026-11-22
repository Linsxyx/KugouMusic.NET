using KaMusic.Cli.Services;
using Microsoft.Extensions.Logging;

namespace KaMusic.Cli.Tests;

// CLI 文件日志测试：验证写入格式、级别过滤、URL 脱敏、体积滚动与旧日志清理。

public sealed class CliLogTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"kamusic-cli-log-test-{Guid.NewGuid():N}");

    public CliLogTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void WritesFormattedEntriesAndFiltersCategoryLoggerToWarningAndAbove()
    {
        CliLog.Initialize(_tempDir);
        try
        {
            CliLog.Info("Program", "启动完成");
            CliLog.Warn("Playback", "加载超时");
            CliLog.Error("Input", "输入异常", new InvalidOperationException("boom"));

            var logger = CliLog.CreateLogger<CliLogTests>();
            Assert.False(logger.IsEnabled(LogLevel.Information));
            Assert.True(logger.IsEnabled(LogLevel.Warning));
            logger.LogInformation("should-not-be-written");
            logger.LogWarning("warn-from-ilogger");
            logger.LogError(new ArgumentException("bad-arg"), "err-from-ilogger");

            var path = CliLog.CurrentLogFilePath;
            Assert.NotNull(path);
            CliLog.Shutdown();

            var content = File.ReadAllText(path!);
            Assert.Contains("INF] Program: 启动完成", content);
            Assert.Contains("WRN] Playback: 加载超时", content);
            Assert.Contains("ERR] Input: 输入异常", content);
            Assert.Contains("InvalidOperationException: boom", content);
            Assert.DoesNotContain("should-not-be-written", content);
            Assert.Contains("WRN] CliLogTests: warn-from-ilogger", content);
            Assert.Contains("ERR] CliLogTests: err-from-ilogger", content);
            Assert.Contains("ArgumentException: bad-arg", content);
        }
        finally
        {
            CliLog.Shutdown();
        }
    }

    [Fact]
    public void RollsBySizeAndCleansUpOnlyCliLogs()
    {
        var desktopLog = Path.Combine(_tempDir, "kg-20260101.log");
        var expiredCliLog = Path.Combine(_tempDir, "cli-20200101.log");
        File.WriteAllText(desktopLog, "desktop");
        File.WriteAllText(expiredCliLog, "old-cli");
        File.SetLastWriteTime(expiredCliLog, DateTime.Now.AddDays(-30));

        CliLog.Initialize(_tempDir, maxFileBytes: 1024, retainedFiles: 5, retainedAge: TimeSpan.FromDays(14));
        try
        {
            Assert.True(File.Exists(desktopLog));
            Assert.False(File.Exists(expiredCliLog));

            var payload = new string('x', 400);
            for (var i = 0; i < 6; i++)
            {
                CliLog.Info("RollTest", $"{i}:{payload}");
            }
        }
        finally
        {
            CliLog.Shutdown();
        }

        var cliLogs = Directory.GetFiles(_tempDir, "cli-*.log");
        Assert.True(cliLogs.Length >= 2);
        Assert.True(File.Exists(desktopLog));
    }

    [Fact]
    public void SanitizeUrlStripsQueryParameters()
    {
        var sanitized = CliLog.SanitizeUrl("https://fs.kugou.com/audio/demo.flac?token=secret123&key=abc");
        Assert.Equal("https://fs.kugou.com/audio/demo.flac", sanitized);
        Assert.Equal("<empty>", CliLog.SanitizeUrl(""));
    }

    public void Dispose()
    {
        CliLog.Shutdown();
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
}
