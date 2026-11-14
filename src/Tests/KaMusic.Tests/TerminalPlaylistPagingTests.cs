using KaMusic.Cli.Services;

namespace KaMusic.Tests;

// 歌单分页与播放队列顺序的回归测试：
// 1) 大歌单必须按服务端总数翻到底，而不是“本页没满就认为没有下一页”；
// 2) 队列顺序在关闭随机播放后必须恢复成歌单原始顺序；
// 3) 歌单里 Hash 相同的重复曲目不能被去重/串位。
public class TerminalPlaylistPagingTests
{
    [Fact]
    public void HasMoreSongs_UsesTotal_SoLargePlaylistsKeepPaging()
    {
        // 1598 首的歌单，每页 300：第 1 页只拿到 100 首（服务端限流）时仍应继续翻页。
        Assert.True(TerminalPlaylistPaging.HasMoreSongs(page: 1, pageSize: 300, pageCount: 100, total: 1598));
        Assert.True(TerminalPlaylistPaging.HasMoreSongs(page: 5, pageSize: 300, pageCount: 300, total: 1598));
        Assert.False(TerminalPlaylistPaging.HasMoreSongs(page: 6, pageSize: 300, pageCount: 98, total: 1598));
    }

    [Fact]
    public void HasMoreSongs_FallsBackToFullPage_WhenTotalMissing()
    {
        Assert.True(TerminalPlaylistPaging.HasMoreSongs(1, 300, 300, 0));
        Assert.False(TerminalPlaylistPaging.HasMoreSongs(1, 300, 120, 0));
        Assert.False(TerminalPlaylistPaging.HasMoreSongs(2, 300, 0, 0));
    }

    [Fact]
    public void ResolveIndex_PrefersReferenceOverDuplicateHash()
    {
        var first = new FakeTrack("hash-a", "A");
        var duplicate = new FakeTrack("hash-a", "A (Live)");
        var list = new List<FakeTrack> { first, duplicate };

        Assert.Equal(0, TerminalPlaylistPaging.ResolveIndex(list, first, static (l, r) => l.Hash == r.Hash));
        Assert.Equal(1, TerminalPlaylistPaging.ResolveIndex(list, duplicate, static (l, r) => l.Hash == r.Hash));
    }

    [Fact]
    public void ResolveIndex_FallsBackToIdentityMatch_AndReturnsMinusOneWhenMissing()
    {
        var list = new List<FakeTrack> { new("hash-a", "A"), new("hash-b", "B") };
        var copy = new FakeTrack("hash-b", "B");

        Assert.Equal(1, TerminalPlaylistPaging.ResolveIndex(list, copy, static (l, r) => l.Hash == r.Hash));
        Assert.Equal(-1, TerminalPlaylistPaging.ResolveIndex(list, new FakeTrack("hash-z", "Z"), static (l, r) => l.Hash == r.Hash));
    }

    [Fact]
    public void ShuffleKeepingCurrent_KeepsEveryTrack_AndPinsCurrentFirst()
    {
        var queue = Enumerable.Range(0, 50).Select(i => new FakeTrack($"hash-{i}", $"T{i}")).ToList();
        var current = queue[17];

        var shuffled = TerminalPlaylistPaging.ShuffleKeepingCurrent(queue, 17, new Random(1234));

        Assert.Equal(queue.Count, shuffled.Count);
        Assert.Same(current, shuffled[0]);
        Assert.Equal(
            queue.Select(t => t.Hash).OrderBy(h => h, StringComparer.Ordinal),
            shuffled.Select(t => t.Hash).OrderBy(h => h, StringComparer.Ordinal));
    }

    [Fact]
    public void ShuffleKeepingCurrent_KeepsDuplicateTracks()
    {
        var queue = new List<FakeTrack>
        {
            new("hash-a", "A"),
            new("hash-a", "A"),
            new("hash-b", "B")
        };

        var shuffled = TerminalPlaylistPaging.ShuffleKeepingCurrent(queue, 0, new Random(7));

        Assert.Equal(3, shuffled.Count);
        Assert.Equal(2, shuffled.Count(t => t.Hash == "hash-a"));
    }

    [Fact]
    public void ShuffleKeepingCurrent_HandlesEmptyAndOutOfRangeIndex()
    {
        Assert.Empty(TerminalPlaylistPaging.ShuffleKeepingCurrent(Array.Empty<FakeTrack>(), 0, new Random(1)));

        var queue = new List<FakeTrack> { new("hash-a", "A") };
        var result = TerminalPlaylistPaging.ShuffleKeepingCurrent(queue, 99, new Random(1));
        Assert.Single(result);
    }

    private sealed record FakeTrack(string Hash, string Name);
}
