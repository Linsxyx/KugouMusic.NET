using System.Collections.Generic;
using System.Threading.Tasks;
using KuGou.Net.Abstractions.Models;

namespace KugouAvaloniaPlayer.Services;

/// <summary>
///     本地歌曲歌手匹配服务。传入本地歌曲的歌手名和在线候选列表，
///     通过对话框让用户选择，返回匹配的在线歌手信息。
/// </summary>
public interface ILocalSingerMatchService
{
    /// <summary>
    ///     以对话框形式展示在线歌手候选列表，让用户选择。
    /// </summary>
    /// <param name="localSinger">本地歌曲的歌手信息（仅有 Name，无 Id）。</param>
    /// <param name="candidates">搜索到的在线歌手候选列表。</param>
    /// <returns>用户选择的在线歌手信息，取消则返回 null。</returns>
    Task<SingerLite?> MatchSingerAsync(SingerLite localSinger, List<SearchAuthorItem> candidates);
}
