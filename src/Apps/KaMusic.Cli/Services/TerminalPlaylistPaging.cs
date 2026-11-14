namespace KaMusic.Cli.Services;

// 歌单分页与播放队列顺序的纯逻辑工具：不依赖网络、音频与 UI，便于单元测试。
internal static class TerminalPlaylistPaging
{
    /// <summary>
    ///     判断当前页之后是否还有歌。服务端会返回歌曲总数（total），有 total 时以 total 为准，
    ///     否则退化为“本页是否满页”。这修掉了“只按本页条数判断”导致大歌单只加载一页的问题。
    /// </summary>
    public static bool HasMoreSongs(int page, int pageSize, int pageCount, int total)
    {
        if (pageCount <= 0 || pageSize <= 0 || page <= 0)
        {
            return false;
        }

        if (total > 0)
        {
            return (long)page * pageSize < total;
        }

        return pageCount >= pageSize;
    }

    /// <summary>
    ///     在列表里定位目标歌曲的索引：先按对象引用精确匹配（歌单里同 Hash 的重复歌曲不会串位），
    ///     再退回按歌曲身份比较。返回 -1 表示未找到。
    /// </summary>
    public static int ResolveIndex<T>(IReadOnlyList<T> list, T? target, Func<T, T, bool> sameTrack)
        where T : class
    {
        if (target is null)
        {
            return -1;
        }

        for (var i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], target))
            {
                return i;
            }
        }

        for (var i = 0; i < list.Count; i++)
        {
            if (sameTrack(list[i], target))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    ///     随机播放时把当前歌曲固定到首位、其余歌曲打乱，并保留重复歌曲（不按身份去重）。
    /// </summary>
    public static List<T> ShuffleKeepingCurrent<T>(IReadOnlyList<T> queue, int currentIndex, Random random)
    {
        var result = new List<T>(queue.Count);
        if (queue.Count == 0)
        {
            return result;
        }

        var index = currentIndex >= 0 && currentIndex < queue.Count ? currentIndex : 0;
        result.Add(queue[index]);

        var rest = new List<T>(queue.Count - 1);
        for (var i = 0; i < queue.Count; i++)
        {
            if (i != index)
            {
                rest.Add(queue[i]);
            }
        }

        for (var i = rest.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (rest[i], rest[j]) = (rest[j], rest[i]);
        }

        result.AddRange(rest);
        return result;
    }
}
