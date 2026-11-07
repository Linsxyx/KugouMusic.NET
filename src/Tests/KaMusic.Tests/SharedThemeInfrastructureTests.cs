using AvaloniaLyrics;
using KugouAvaloniaPlayer.Views.NowPlayingThemes;

namespace AvaloniaSilkEffects.Tests;

public sealed class SharedThemeInfrastructureTests
{
    [Fact]
    public void SnapshotKeepsBlankLineBoundariesAndClampsWordTimingForEveryTheme()
    {
        LyricLine[] source =
        [
            new() { Text = "歌词", Start = TimeSpan.FromSeconds(-2), Words = [new() { Text = "词", Start = TimeSpan.FromSeconds(-3) }] },
            new() { Text = " ", Start = TimeSpan.FromSeconds(2) },
            new() { Text = "尾声", Start = TimeSpan.FromSeconds(8), Duration = TimeSpan.FromSeconds(0.01) },
            new() { Text = "最后", Start = TimeSpan.FromSeconds(10) },
        ];
        var snapshot = new NowPlayingLyricSnapshot(source, 30);
        var lumiere = snapshot.LumiereLines;
        var sonnet = snapshot.SonnetLines;
        Assert.Equal(3, lumiere.Count);
        Assert.Equal(lumiere.Select(l => (l.Text, l.StartTime, l.EndTime)), sonnet.Select(l => (l.FullText, l.StartTime, l.EndTime)));
        Assert.Equal(0, lumiere[0].StartTime);
        Assert.Equal(2, lumiere[0].EndTime);
        Assert.Equal(0, lumiere[0].Words[0].StartTime);
        Assert.Equal(0.01, lumiere[0].Words[0].EndTime);
        Assert.Equal(8.1, lumiere[1].EndTime, 6);
        Assert.Equal(30, lumiere[2].EndTime);
        Assert.Same(lumiere, snapshot.LumiereLines);
        Assert.Same(sonnet, snapshot.SonnetLines);
    }

    [Fact]
    public void SnapshotPreservesDistinctInstrumentalFallbacks()
    {
        var snapshot = new NowPlayingLyricSnapshot([new() { Text = " " }], 0);
        Assert.Empty(snapshot.LumiereLines);
        Assert.NotEmpty(snapshot.SonnetLines);
    }

    [Fact]
    public void MaintenanceRetiresOneAtATimeThenBuildsNextCreditsAndPrevious()
    {
        var released = new List<int>();
        using var cache = new EffectParagraphCache<TrackedScene>();
        cache[0] = new(0, released); cache[1] = new(1, released);
        cache.RetireAll();
        cache[2] = new(2, released);
        Assert.Equal(EffectCacheWorkKind.None, cache.NextWork(2, 5, true).Kind);
        Assert.Equal(new[] { 0 }, released);
        Assert.Equal(EffectCacheWorkKind.None, cache.NextWork(2, 5, true).Kind);
        Assert.Equal(new[] { 0, 1 }, released);
        Assert.Equal(new(EffectCacheWorkKind.Paragraph, 3), cache.NextWork(2, 5, true));
        cache[3] = new(3, released);
        Assert.Equal(EffectCacheWorkKind.Credits, cache.NextWork(2, 5, true).Kind);
        Assert.Equal(new(EffectCacheWorkKind.Paragraph, 1), cache.NextWork(2, 5, false));
        cache.Dispose();
        Assert.Equal(new[] { 0, 1, 2, 3 }, released);
        cache.Dispose();
        Assert.Equal(4, released.Count);
    }

    private sealed class TrackedScene(int id, List<int> released) : IDisposable
    {
        public void Dispose() => released.Add(id);
    }
}
