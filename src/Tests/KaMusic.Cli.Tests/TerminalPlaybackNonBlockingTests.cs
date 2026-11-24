using System.Diagnostics;
using KaMusic.Cli.Models;
using KaMusic.Cli.Services;
using KaMusic.Cli.Tui;
using Microsoft.Data.Sqlite;

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

    [Fact]
    public async Task LocalMusicGetPlaylists_DoesNotBlockUiThread_WhenSqliteDatabaseIsLocked()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"kamusic-sqlite-lock-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "kugou-player.db");

        try
        {
            var service = new TerminalLocalMusicService(dbPath);
            await service.CreatePlaylistAsync("锁测试歌单");

            await using var locker = new SqliteConnection($"Data Source={dbPath}");
            await locker.OpenAsync();
            await using var lockCmd = locker.CreateCommand();
            lockCmd.CommandText = "BEGIN EXCLUSIVE;";
            await lockCmd.ExecuteNonQueryAsync();

            var previous = SynchronizationContext.Current;
            var dispatcher = new UiDispatcher();
            Task<IReadOnlyList<TerminalPlaylistItem>> queryTask;
            var sw = Stopwatch.StartNew();
            SynchronizationContext.SetSynchronizationContext(dispatcher);
            try
            {
                queryTask = service.GetPlaylistsAsync();
            }
            finally
            {
                sw.Stop();
                SynchronizationContext.SetSynchronizationContext(previous);
            }

            // 即使 SQLite 被独占锁住，调用线程（UI 主循环）也必须立即返回，不能同步等待。
            Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(200), $"GetPlaylistsAsync 在主线程阻塞了 {sw.Elapsed.TotalMilliseconds:F0}ms");
            Assert.False(queryTask.IsCompleted);

            await using (var unlockCmd = locker.CreateCommand())
            {
                unlockCmd.CommandText = "COMMIT;";
                await unlockCmd.ExecuteNonQueryAsync();
            }

            var playlists = await queryTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(playlists, p => p.Name == "锁测试歌单");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
