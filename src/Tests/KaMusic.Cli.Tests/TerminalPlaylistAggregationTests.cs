using System.Text.Json;
using System.Text.Json.Nodes;
using KaMusic.Cli.Models;
using KuGou.Net.Protocol.Transport;

namespace KaMusic.Cli.Tests;

public sealed class TerminalPlaylistAggregationTests
{
    [Theory]
    [InlineData("Online", 200)]
    [InlineData("Discover", 200)]
    [InlineData("Album", 50)]
    [InlineData("Rank", 100)]
    public async Task LargePlaylist_UsesPcPageSizesAndContiguousRequests_PreservingAllOccurrences(string kindName, int pageSize)
    {
        var kind = Enum.Parse<TerminalPlaylistKind>(kindName);
        const int count = 1598;
        using var test = new TerminalTestContext(request => Page(request, kind, count));
        var progress = new List<int>();

        var songs = await test.Data.GetAllPlaylistSongsAsync(Playlist(kind), new InlineProgress(progress.Add));

        Assert.Equal(Enumerable.Range(0, count).Select(i => $"Song {i}"), songs.Select(x => x.Name));
        Assert.Equal(2, songs.Count(x => x.Hash == "hash-1"));
        var pageCount = (count + pageSize - 1) / pageSize;
        Assert.Equal(Enumerable.Range(1, pageCount).Select(page => Math.Min(count, page * pageSize)), progress);
        Assert.Equal(pageCount, test.Transport.Requests.Count);
        for (var i = 0; i < pageCount; i++)
        {
            var (offset, requestedSize) = Window(test.Transport.Requests[i], kind);
            Assert.Equal(i * pageSize, offset);
            Assert.Equal(pageSize, requestedSize);
        }
    }

    [Theory]
    [InlineData("Online", 200)]
    [InlineData("Album", 50)]
    [InlineData("Rank", 100)]
    public async Task MissingTotal_AggregatesFullPagesAndFinalPartialPage(string kindName, int pageSize)
    {
        var kind = Enum.Parse<TerminalPlaylistKind>(kindName);
        var count = pageSize * 2 + 7;
        using var test = new TerminalTestContext(request => Page(request, kind, count, reportedTotal: 0));

        var songs = await test.Data.GetAllPlaylistSongsAsync(Playlist(kind));

        Assert.Equal(Enumerable.Range(0, count).Select(i => $"Song {i}"), songs.Select(x => x.Name));
        Assert.Equal(3, test.Transport.Requests.Count);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(400, 3)]
    public async Task EmptyPage_StopsEvenWhenTotalIsStale(int actualCount, int expectedRequests)
    {
        using var test = new TerminalTestContext(request => Page(request, TerminalPlaylistKind.Online, actualCount, reportedTotal: 1598));
        var songs = await test.Data.GetAllPlaylistSongsAsync(Playlist(TerminalPlaylistKind.Online));
        Assert.Equal(actualCount, songs.Count);
        Assert.Equal(expectedRequests, test.Transport.Requests.Count);
    }

    [Fact]
    public async Task ExactMultipleOfKnownTotal_DoesNotFetchAnExtraPage()
    {
        using var test = new TerminalTestContext(request => Page(request, TerminalPlaylistKind.Online, 400));
        Assert.Equal(400, (await test.Data.GetAllPlaylistSongsAsync(Playlist(TerminalPlaylistKind.Online))).Count);
        Assert.Equal(2, test.Transport.Requests.Count);
    }

    [Fact]
    public async Task TopCard_IsReturnedOnce_WithoutNetworkOrDuplicateAggregation()
    {
        using var test = new TerminalTestContext(_ => throw new InvalidOperationException("Unexpected network request"));
        var songs = Enumerable.Range(0, 600).Select(i => new TerminalSongItem { Name = $"Song {i}" }).ToArray();
        var playlist = new TerminalPlaylistItem { Kind = TerminalPlaylistKind.TopCard, PreloadedSongs = songs };
        Assert.Equal(songs, await test.Data.GetAllPlaylistSongsAsync(playlist));
        Assert.Empty(await test.Data.GetPlaylistSongsAsync(playlist, 2));
        Assert.Empty(test.Transport.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancellation_StopsBeforeNextRequest(bool cancelBeforeStarting)
    {
        using var cancellation = new CancellationTokenSource();
        using var test = new TerminalTestContext(request => Page(request, TerminalPlaylistKind.Online, 1598));
        if (cancelBeforeStarting)
            cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test.Data.GetAllPlaylistSongsAsync(
            Playlist(TerminalPlaylistKind.Online), new InlineProgress(_ => cancellation.Cancel()), cancellation.Token));
        Assert.Equal(cancelBeforeStarting ? 0 : 1, test.Transport.Requests.Count);
    }

    [Fact]
    public async Task Likes_AggregatesBeyondFirstPage_UsingPcFavoritePageSize()
    {
        using var test = new TerminalTestContext(request => request.Path.Contains("get_all_list", StringComparison.Ordinal)
            ? TerminalTestContext.Json("""{"status":1,"data":{"info":[{"name":"我喜欢","is_def":2,"listid":2,"list_create_gid":"likes"}]}}""")
            : Page(request, TerminalPlaylistKind.Online, 1598));
        // In-memory test credentials only; no session persistence or remote request.
        test.Clients.SessionManager.UpdateAuth("1", "test-session", "0", "", null);

        await test.Data.EnsureLikeListLoadedAsync();

        Assert.True(test.Data.IsLiked("hash-0"));
        Assert.True(test.Data.IsLiked("hash-1597"));
        Assert.False(test.Data.IsLiked("missing"));
        var requests = test.Transport.Requests.Where(x => x.Params.ContainsKey("begin_idx")).ToArray();
        Assert.Equal(6, requests.Length);
        Assert.Equal(Enumerable.Range(0, 6).Select(i => (i * 300, 300)), requests.Select(x => Window(x, TerminalPlaylistKind.Online)));
    }

    [Fact]
    public async Task LoadedLargePlaylist_CanSelectLateDuplicate_ThenShuffleInsertAndRestore()
    {
        using var test = new TerminalTestContext(request => request.Params.ContainsKey("begin_idx")
            ? Page(request, TerminalPlaylistKind.Online, 1598)
            : TerminalTestContext.Json("{\"status\":0}"), shuffle: true);
        var songs = await test.Data.GetAllPlaylistSongsAsync(Playlist(TerminalPlaylistKind.Online));
        await test.Playback.PlayAsync(songs[1201], songs);
        Assert.Equal("Song 1201", test.Playback.CurrentSong!.Name);
        test.Playback.EnqueueNext(new TerminalSongItem { Name = "Next", Hash = "new" });
        test.Playback.ToggleShuffle();
        Assert.Equal(1599, test.Playback.Queue.Count);
        Assert.Equal("Song 1201", test.Playback.Queue[1201].Name);
        Assert.Equal("Next", test.Playback.Queue[1202].Name);
        Assert.Equal("Song 1597", test.Playback.Queue[^1].Name);
        await test.Playback.PlayNextAsync();
        Assert.Equal("Next", test.Playback.CurrentSong!.Name);
    }

    private static TerminalPlaylistItem Playlist(TerminalPlaylistKind kind) => new() { Kind = kind, Id = "test-playlist", RankId = 1 };

    private static (int Offset, int PageSize) Window(KgRequest request, TerminalPlaylistKind kind)
    {
        if (kind is TerminalPlaylistKind.Online or TerminalPlaylistKind.Discover)
        {
            Assert.Equal("/pubsongs/v2/get_other_list_file_nofilt", request.Path);
            return (int.Parse(request.Params["begin_idx"]), int.Parse(request.Params["pagesize"]));
        }
        Assert.Equal(kind == TerminalPlaylistKind.Album ? "/v1/album_audio/lite" : "/openapi/kmr/v2/rank/audio", request.Path);
        var body = Assert.IsType<JsonObject>(request.Body);
        var page = body["page"]!.GetValue<int>();
        var size = body["pagesize"]!.GetValue<int>();
        return ((page - 1) * size, size);
    }

    private static JsonElement Page(KgRequest request, TerminalPlaylistKind kind, int count, int? reportedTotal = null)
    {
        var (offset, size) = Window(request, kind);
        var items = Enumerable.Range(offset, Math.Max(0, Math.Min(size, count - offset))).Select(i =>
        {
            var hash = i == 1201 ? "hash-1" : $"hash-{i}";
            return kind switch
            {
                TerminalPlaylistKind.Album => (object)new { @base = new { audio_name = $"Song {i}" }, audio_info = new { hash } },
                TerminalPlaylistKind.Rank => new { songname = $"Song {i}", deprecated = new { hash } },
                _ => new { name = $"Song {i}", hash, fileid = i + 1 }
            };
        }).ToArray();
        var data = new Dictionary<string, object>
        {
            [kind == TerminalPlaylistKind.Rank ? "songlist" : "songs"] = items,
            [kind is TerminalPlaylistKind.Online or TerminalPlaylistKind.Discover ? "count" : "total"] = reportedTotal ?? count
        };
        return JsonSerializer.SerializeToElement(new { status = 1, data });
    }

    private sealed class InlineProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
