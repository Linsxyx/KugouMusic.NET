using System;
using System.Collections.Generic;
using ZLinq;
using Avalonia.Collections;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services;

public class PlaybackQueueManager
{
    private const int MaxQueueSize = 1000;
    private readonly Random _random = new();

    public AvaloniaList<SongItem> PlaybackQueue { get; } = new();
    public List<SongItem> OriginalQueue { get; } = new();

    public bool IsRepeatOneMode { get; private set; }
    public bool IsShuffleMode { get; private set; }

    /// <summary>
    ///     初始化或替换播放队列
    /// </summary>
    public void SetupQueue(SongItem song, IList<SongItem>? contextList)
    {
        if (contextList is { Count: > 0 })
        {
            OriginalQueue.Clear();
            PlaybackQueue.Clear();

            var finalList = TakeWindowAround(contextList, song, MaxQueueSize);

            OriginalQueue.AddRange(finalList);

            if (IsShuffleMode)
            {
                var shuffleList = new List<SongItem>(finalList);
                shuffleList.Remove(song);

                ShuffleInPlace(shuffleList);

                PlaybackQueue.Add(song);
                PlaybackQueue.AddRange(shuffleList);
            }
            else
            {
                PlaybackQueue.AddRange(finalList);
            }
        }
        else if (PlaybackQueue.Count == 0)
        {
            OriginalQueue.Add(song);
            PlaybackQueue.Add(song);
        }
    }

    public SongItem? GetNext(SongItem? currentPlaying)
    {
        if (PlaybackQueue.Count == 0) return null;
        var idx = currentPlaying != null ? PlaybackQueue.IndexOf(currentPlaying) : -1;
        var nextIdx = (idx + 1) % PlaybackQueue.Count;
        return PlaybackQueue[nextIdx];
    }

    public SongItem? GetPrevious(SongItem? currentPlaying)
    {
        if (PlaybackQueue.Count == 0) return null;
        var idx = currentPlaying != null ? PlaybackQueue.IndexOf(currentPlaying) : -1;
        var prevIdx = idx - 1;
        if (prevIdx < 0) prevIdx = PlaybackQueue.Count - 1;
        return PlaybackQueue[prevIdx];
    }

    public void RestoreQueue(IReadOnlyList<SongItem> songs)
    {
        OriginalQueue.Clear();
        PlaybackQueue.Clear();

        if (songs.Count == 0)
            return;

        OriginalQueue.AddRange(songs);
        PlaybackQueue.AddRange(songs);
    }

    public void AddToNext(SongItem song, SongItem? currentPlaying)
    {
        PlaybackQueue.Remove(song);
        OriginalQueue.Remove(song);

        var originalIdx = currentPlaying != null ? OriginalQueue.IndexOf(currentPlaying) : -1;
        if (originalIdx >= 0 && originalIdx < OriginalQueue.Count)
            OriginalQueue.Insert(originalIdx + 1, song);
        else
            OriginalQueue.Add(song);

        var playIdx = currentPlaying != null ? PlaybackQueue.IndexOf(currentPlaying) : -1;
        if (playIdx >= 0 && playIdx < PlaybackQueue.Count)
            PlaybackQueue.Insert(playIdx + 1, song);
        else
            PlaybackQueue.Add(song);
    }

    public void AddToEnd(IReadOnlyList<SongItem> songs)
    {
        if (songs.Count == 0)
            return;

        OriginalQueue.AddRange(songs);
        PlaybackQueue.AddRange(songs);
    }

    public void Remove(SongItem song)
    {
        PlaybackQueue.Remove(song);
        OriginalQueue.Remove(song);
    }

    public void Clear()
    {
        PlaybackQueue.Clear();
        OriginalQueue.Clear();
    }

    public bool ToggleRepeatOne(SongItem? currentSong)
    {
        IsRepeatOneMode = !IsRepeatOneMode;
        if (IsRepeatOneMode && IsShuffleMode)
            SetShuffleMode(false, currentSong);

        return IsRepeatOneMode;
    }

    public bool ToggleShuffle(SongItem? currentSong)
    {
        SetShuffleMode(!IsShuffleMode, currentSong);
        if (IsShuffleMode)
            IsRepeatOneMode = false;

        return IsShuffleMode;
    }

    private void SetShuffleMode(bool value, SongItem? currentSong)
    {
        if (IsShuffleMode == value)
            return;

        IsShuffleMode = value;
        if (PlaybackQueue.Count == 0)
            return;

        if (IsShuffleMode)
        {
            var songsToShuffle = PlaybackQueue.AsValueEnumerable().Where(x => x != currentSong).ToList();
            var n = songsToShuffle.Count;
            while (n > 1)
            {
                n--;
                var k = _random.Next(n + 1);
                (songsToShuffle[k], songsToShuffle[n]) = (songsToShuffle[n], songsToShuffle[k]);
            }

            PlaybackQueue.Clear();
            if (currentSong != null) PlaybackQueue.Add(currentSong);
            PlaybackQueue.AddRange(songsToShuffle);
        }
        else
        {
            if (OriginalQueue.Count != PlaybackQueue.Count)
            {
                OriginalQueue.Clear();
                OriginalQueue.AddRange(PlaybackQueue);
            }

            PlaybackQueue.Clear();
            PlaybackQueue.AddRange(OriginalQueue);
        }
    }
    
    private static List<SongItem> TakeWindowAround(
        IList<SongItem> source,
        SongItem current,
        int maxCount)
    {
        if (source.Count <= maxCount)
            return source.AsValueEnumerable().ToList();

        var currentIndex = source.IndexOf(current);
        if (currentIndex < 0)
        {
            return source
                .AsValueEnumerable()
                .Take(maxCount)
                .ToList();
        }
        
        var before = maxCount / 2;
        var after = maxCount - before - 1;

        var start = currentIndex - before;
        var end = currentIndex + after;
        
        if (start < 0)
        {
            end += -start;
            start = 0;
        }
        
        if (end >= source.Count)
        {
            var overflow = end - source.Count + 1;
            start = Math.Max(0, start - overflow);
            end = source.Count - 1;
        }

        var count = end - start + 1;

        return source
            .AsValueEnumerable()
            .Skip(start)
            .Take(count)
            .ToList();
    }
    
    private void ShuffleInPlace(List<SongItem> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
