using System.Collections.Generic;
using KugouAvaloniaPlayer.Models;

namespace KugouAvaloniaPlayer.Services;

public static class LyricsEngine
{
    /// <summary>
    ///     纯函数：根据时间获取当前歌词
    /// </summary>
    /// <param name="timeMs">当前播放时间（毫秒）</param>
    /// <param name="lyrics">排好序的歌词列表</param>
    /// <returns>当前的歌词对象，如果没有则返回 null</returns>
    public static LyricEntry? GetCurrentLyric(double timeMs, List<LyricEntry>? lyrics)
    {
        if (lyrics == null || lyrics.Count == 0)
            return null;

        // 边界情况：时间小于第一句，返回第一句（或者 null，看需求）
        if (timeMs < lyrics[0].TimeMs)
            return lyrics[0];

        // 边界情况：时间大于最后一句，返回最后一句
        if (timeMs >= lyrics[lyrics.Count - 1].TimeMs)
            return lyrics[lyrics.Count - 1];

        // --- 核心：二分查找 (O(log N)) ---
        // 我们要找的是：最后一个 TimeMs <= current time 的元素

        var left = 0;
        var right = lyrics.Count - 1;
        var resultIndex = 0;

        while (left <= right)
        {
            var mid = left + (right - left) / 2;

            if (lyrics[mid].TimeMs <= timeMs)
            {
                // mid 小于当前时间，可能是我们要找的，但也可能后面还有更接近的
                resultIndex = mid;
                left = mid + 1;
            }
            else
            {
                // mid 大于当前时间，肯定不是我们要找的，往左找
                right = mid - 1;
            }
        }

        return lyrics[resultIndex];
    }
}