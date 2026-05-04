using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Collections;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services;

public class PlaybackQueueManager
{
    private readonly Random _random = new();

    public AvaloniaList<SongItem> PlaybackQueue { get; } = new();
    public List<SongItem> OriginalQueue { get; } = new();

    public bool IsShuffleMode { get; private set; }

    /// <summary>
    ///     初始化或替换播放队列
    /// </summary>
    public void SetupQueue(SongItem song, IList<SongItem>? contextList)
    {
        if (contextList != null && contextList.Any())
        {
            const int maxQueueSize = 300;
            OriginalQueue.Clear();
            PlaybackQueue.Clear();

            IEnumerable<SongItem> targetList = contextList;

            // 列表过大时截取当前歌曲附近的部分，防止内存爆炸
            if (contextList.Count > maxQueueSize)
            {
                var currentIndex = contextList.IndexOf(song);
                if (currentIndex >= 0)
                {
                    var start = Math.Max(0, currentIndex - maxQueueSize / 2);
                    var count = Math.Min(contextList.Count - start, maxQueueSize);
                    targetList = contextList.Skip(start).Take(count);
                }
                else
                {
                    targetList = contextList.Take(maxQueueSize);
                }
            }

            var finalList = targetList.ToList();
            OriginalQueue.AddRange(finalList);

            if (IsShuffleMode)
            {
                var shuffleList = new List<SongItem>(finalList);
                shuffleList.Remove(song);

                var n = shuffleList.Count;
                while (n > 1)
                {
                    n--;
                    var k = _random.Next(n + 1);
                    (shuffleList[k], shuffleList[n]) = (shuffleList[n], shuffleList[k]);
                }

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

    public bool ToggleShuffle(SongItem? currentSong)
    {
        IsShuffleMode = !IsShuffleMode;
        if (PlaybackQueue.Count == 0) return IsShuffleMode;

        if (IsShuffleMode)
        {
            var songsToShuffle = PlaybackQueue.Where(x => x != currentSong).ToList();
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
            if (OriginalQueue.Count != PlaybackQueue.Count) OriginalQueue.Clear(); // 安全防御
            PlaybackQueue.Clear();
            PlaybackQueue.AddRange(OriginalQueue);
        }

        return IsShuffleMode;
    }
}