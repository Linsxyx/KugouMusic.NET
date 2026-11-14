using KaMusic.Cli.Models;

namespace KaMusic.Cli.Tests;

public sealed class TerminalPlaybackQueueTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateSelection_Shuffle_Insert_PlayFromQueue_Remove_Restore_AndNavigate(bool startShuffled)
    {
        using var test = new TerminalTestContext(shuffle: startShuffled);
        var playback = test.Playback;
        TerminalSongItem[] songs = [Song("a", "A"), Song("duplicate", "First"), Song("b", "B"),
            Song("duplicate", "Second"), Song("c", "C")];

        await playback.PlayAsync(songs[3], songs);
        Assert.Equal("Second", playback.CurrentSong!.Name);
        if (!startShuffled)
            playback.ToggleShuffle();
        var selected = playback.CurrentSong;
        Assert.Same(selected, playback.Queue[0]);
        Assert.Equal(5, playback.Queue.Count);

        playback.EnqueueNext(Song("next", "Next"));
        var next = playback.Queue[1];
        Assert.Equal("Next", next.Name);
        await playback.PlayNextAsync();
        Assert.Same(next, playback.CurrentSong);
        await playback.PlayPreviousAsync();
        Assert.Same(selected, playback.CurrentSong);

        // Queue-page selection must preserve both queue orders and each occurrence's identity.
        var shuffled = playback.Queue.ToArray();
        await playback.PlayAsync(next);
        Assert.Equal(shuffled, playback.Queue);
        await playback.PlayAsync(selected!);
        var firstDuplicateIndex = playback.Queue.ToList().FindIndex(x => x.Name == "First");
        playback.RemoveFromQueueAt(firstDuplicateIndex);
        Assert.Same(selected, playback.CurrentSong);
        playback.ToggleShuffle();

        Assert.Equal(new[] { "A", "B", "Second", "Next", "C" }, playback.Queue.Select(x => x.Name));
        Assert.Same(selected, playback.CurrentSong);
        Assert.True(selected!.IsPlaying);
        Assert.Single(playback.Queue, x => x.IsPlaying);
        await playback.PlayNextAsync();
        Assert.Same(next, playback.CurrentSong);
        await playback.PlayPreviousAsync();
        Assert.Same(selected, playback.CurrentSong);
    }

    [Fact]
    public async Task PlayingFromQueueContext_DoesNotReplaceOriginalOrderOrReshuffle()
    {
        using var test = new TerminalTestContext();
        var playback = test.Playback;
        var songs = Enumerable.Range(0, 8).Select(i => Song($"h{i}", $"Song {i}")).ToArray();
        await playback.PlayAsync(songs[5], songs);
        playback.ToggleShuffle();
        var shuffled = playback.Queue.ToArray();
        var selected = playback.Queue[3];

        await playback.PlayAsync(selected, playback.Queue);

        Assert.Equal(shuffled, playback.Queue);
        Assert.Same(selected, playback.CurrentSong);
        playback.ToggleShuffle();
        Assert.Equal(songs.Select(x => x.Name), playback.Queue.Select(x => x.Name));
        Assert.Same(selected, playback.CurrentSong);
    }

    [Fact]
    public async Task RemovingSecondDuplicate_LeavesFirstOccurrenceInOriginalPosition()
    {
        using var test = new TerminalTestContext();
        var playback = test.Playback;
        TerminalSongItem[] songs = [Song("same", "First"), Song("middle", "Middle"), Song("same", "Second")];
        await playback.PlayAsync(songs[1], songs);
        playback.ToggleShuffle();
        playback.RemoveFromQueueAt(playback.Queue.ToList().FindIndex(x => x.Name == "Second"));
        playback.ToggleShuffle();
        Assert.Equal(new[] { "First", "Middle" }, playback.Queue.Select(x => x.Name));
        Assert.Equal("Middle", playback.CurrentSong!.Name);
        await playback.PlayPreviousAsync();
        Assert.Equal("First", playback.CurrentSong!.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnqueueExistingOccurrence_MovesItLikePc_WithoutDuplicatingOrMovingItsTwin(bool fromPlaylist)
    {
        using var test = new TerminalTestContext();
        var playback = test.Playback;
        TerminalSongItem[] songs = [Song("same", "First"), Song("same", "Second"), Song("current", "Current"), Song("tail", "Tail")];
        await playback.PlayAsync(songs[2], songs);
        var selected = playback.Queue[1];
        var current = playback.CurrentSong;
        playback.ToggleShuffle();
        playback.EnqueueNext(fromPlaylist ? songs[1] : selected);
        Assert.Equal(4, playback.Queue.Count);
        Assert.Same(selected, playback.Queue[1]);
        Assert.Same(current, playback.CurrentSong);
        playback.ToggleShuffle();
        Assert.Equal(new[] { "First", "Current", "Second", "Tail" }, playback.Queue.Select(x => x.Name));
        await playback.PlayNextAsync();
        Assert.Same(selected, playback.CurrentSong);
    }

    [Fact]
    public async Task UnrelatedSongWithoutContext_ReplacesQueue_AndNavigationWraps()
    {
        using var test = new TerminalTestContext();
        var playback = test.Playback;
        TerminalSongItem[] songs = [Song("a", "A"), Song("b", "B")];
        await playback.PlayAsync(songs[0], songs);
        await playback.PlayPreviousAsync();
        Assert.Equal("B", playback.CurrentSong!.Name);
        await playback.PlayNextAsync();
        Assert.Equal("A", playback.CurrentSong!.Name);
        await playback.PlayAsync(Song("new", "New"));
        playback.ToggleShuffle();
        playback.ToggleShuffle();
        Assert.Equal("New", Assert.Single(playback.Queue).Name);
        Assert.Same(playback.Queue[0], playback.CurrentSong);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlaylistOccurrence_CanBeSelectedWithoutContext_AfterCloning(bool local)
    {
        using var test = new TerminalTestContext();
        TerminalSongItem[] songs = [Song("same", "First"), Song("same", "Second")];
        if (local)
        {
            foreach (var song in songs)
                song.LocalFilePath = Path.Combine(Path.GetTempPath(), "missing-test-song.wav");
        }
        await test.Playback.PlayAsync(songs[0], songs);
        await test.Playback.PlayAsync(songs[1]);
        Assert.Equal("Second", test.Playback.CurrentSong!.Name);
        test.Playback.ToggleShuffle();
        test.Playback.ToggleShuffle();
        Assert.Equal("Second", test.Playback.CurrentSong.Name);
        Assert.Equal(2, test.Playback.Queue.Count);
    }

    [Fact]
    public async Task QueueBuiltBeforePlayback_IsPreservedWhenSelectingAnEntry()
    {
        using var test = new TerminalTestContext();
        test.Playback.EnqueueNext(Song("a", "A"));
        test.Playback.EnqueueNext(Song("b", "B"));
        Assert.Null(test.Playback.CurrentSong);
        var selected = test.Playback.Queue[1];
        await test.Playback.PlayAsync(selected);
        Assert.Equal(2, test.Playback.Queue.Count);
        Assert.Same(selected, test.Playback.CurrentSong);
        await test.Playback.PlayNextAsync();
        Assert.Equal("A", test.Playback.CurrentSong!.Name);
    }

    [Fact]
    public async Task PersonalFm_PreservesSessionOrderUntilQueueSelectionReturnsToNormalPlayback()
    {
        using var test = new TerminalTestContext();
        TerminalSongItem[] songs = [Song("a", "A"), Song("b", "B"), Song("c", "C")];
        Assert.True(await test.Playback.StartPersonalFmAsync(songs, default, default));
        var fmQueue = test.Playback.Queue.ToArray();
        test.Playback.ToggleShuffle();
        test.Playback.ToggleShuffle();
        Assert.Equal(fmQueue, test.Playback.Queue);
        Assert.True(test.PersonalFm.IsActive);
        test.Playback.EnqueueNext(Song("next", "Next"));
        Assert.Equal(new[] { "A", "Next", "B", "C" }, test.Playback.Queue.Select(x => x.Name));

        var selected = test.Playback.Queue[2];
        await test.Playback.PlayAsync(selected);
        Assert.False(test.PersonalFm.IsActive);
        test.Playback.ToggleShuffle();
        Assert.Same(selected, test.Playback.Queue[0]);
        test.Playback.ToggleShuffle();
        Assert.Equal(new[] { "A", "Next", "B", "C" }, test.Playback.Queue.Select(x => x.Name));
        Assert.Same(selected, test.Playback.CurrentSong);
    }

    private static TerminalSongItem Song(string hash, string name) => new() { Hash = hash, Name = name };
}
