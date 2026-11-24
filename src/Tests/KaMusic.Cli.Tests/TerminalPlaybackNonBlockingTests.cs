using System.Diagnostics;
using KaMusic.Cli.Models;
using KaMusic.Cli.Tui;

namespace KaMusic.Cli.Tests;

// 验证播放控制服务的关键入口（本地音频加载与进度跳转）均不在 TUI 主循环线程上同步阻塞。
public sealed class TerminalPlaybackNonBlockingTests
{
    [Fact]
    public async Task PlayLocalSong_OffloadsLoadOffCallerThread_WithoutBlockingUiLoop()
    {
        using var test = new TerminalTestContext();
        var missingFile = Path.Combine(Path.GetTempPath(), $"kamusic-nonblocking-{Guid.NewGuid():N}.wav");
        var song = new TerminalSongItem
        {
            Hash = "local-nonblocking",
            Name = "Local NonBlocking",
            LocalFilePath = missingFile
        };

        var previous = SynchronizationContext.Current;
        var dispatcher = new UiDispatcher();
        SynchronizationContext.SetSynchronizationContext(dispatcher);
        try
        {
            var sw = Stopwatch.StartNew();
            var playTask = test.Playback.PlayAsync(song, [song]);
            sw.Stop();

            // 同步前导段只更新队列与状态即切到后台线程加载，调用线程应立即返回。
            Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(200), $"PlayAsync 同步前导耗时过长: {sw.Elapsed.TotalMilliseconds:F0}ms");

            // 驱动 UiDispatcher 直到后台加载完成并投递续体。
            var deadline = Environment.TickCount64 + 5000;
            while (!playTask.IsCompleted && Environment.TickCount64 < deadline)
            {
                dispatcher.Drain();
                dispatcher.Wait(10);
            }

            dispatcher.Drain();
            Assert.True(playTask.IsCompleted, "PlayAsync 未在预期时间内完成。");
            await playTask;
            Assert.Equal("Local NonBlocking", test.Playback.CurrentSong?.Name);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public void Seek_ReturnsImmediately_WhenNoTrackIsPlaying()
    {
        using var test = new TerminalTestContext();
        var sw = Stopwatch.StartNew();
        test.Playback.SeekToRatio(0.5);
        test.Playback.SeekRelative(TimeSpan.FromSeconds(10));
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(100));
    }
}
