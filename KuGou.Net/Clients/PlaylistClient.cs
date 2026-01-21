using System.Text.Json;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Adapters.Common;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using KuGou.Net.util;

namespace KuGou.Net.Clients;

public class PlaylistClient(RawPlaylistApi rawApi, KgSessionManager sessionManager)
{
    /// <summary>
    ///     获取歌单内的歌曲列表
    /// </summary>
    public async Task<List<PlaylistSong>> GetSongsAsync(string playlistId, int page = 1, int pageSize = 30)
    {
        // 逻辑搬运：计算起始索引
        var beginIdx = (page - 1) * pageSize;

        var json = await rawApi.GetPlaylistSongsAsync(playlistId, beginIdx, pageSize);
        var data = KgApiResponseParser.Parse<PlaylistSongResponse>(json, AppJsonContext.Default.PlaylistSongResponse);

        return data?.Songs ?? new List<PlaylistSong>();
        //return json;
    }

    /// <summary>
    ///     获取歌单详情（标题、简介、封面等）
    /// </summary>
    public async Task<PlaylistInfo?> GetInfoAsync(string playlistId)
    {
        var session = sessionManager.Session;
        var userid = string.IsNullOrEmpty(session.UserId) ? "0" : session.UserId;
        var token = session.Token ?? "";

        var json = await rawApi.GetPlaylistInfoAsync(playlistId, userid, token);
        var list = KgApiResponseParser.Parse<List<PlaylistInfo>>(json, AppJsonContext.Default.ListPlaylistInfo);
        return list?.FirstOrDefault();
    }


    public async Task<JsonElement?> GetTagsAsync()
    {
        return await rawApi.GetPlaylistTagsAsync();
    }


    private (string UserId, string Token) GetAuth()
    {
        var s = sessionManager.Session;
        if (string.IsNullOrEmpty(s.Token) || s.UserId == "0")
            throw new UnauthorizedAccessException("需要登录后才能操作歌单。");
        return (s.UserId, s.Token);
    }

    /// <summary>
    ///     收藏歌单 / 新建歌单
    ///     <para>对应: /playlist/add</para>
    /// </summary>
    /// <param name="name">歌单名称</param>
    /// <param name="sourceUserId">原歌单创建者ID (收藏时必填)</param>
    /// <param name="sourceListId">原歌单ID (收藏时必填)</param>
    /// <param name="sourceGlobalId">原歌单 GlobalID (可选)</param>
    public async Task<JsonElement?> CollectPlaylistAsync(
        string name,
        string sourceUserId,
        string sourceListId,
        string sourceGlobalId = "",
        long type = 0)
    {
        var (uid, token) = GetAuth();

        // 调用 RawApi
        return await rawApi.CollectPlaylistAsync(uid, token, sourceUserId, sourceListId, sourceGlobalId, name, type);
    }

    /// <summary>
    ///     取消收藏 / 删除歌单
    ///     <para>对应: /playlist/del</para>
    /// </summary>
    /// <param name="listId">用户歌单 ListID (注意不是 GlobalID，是数字ID)</param>
    public async Task<JsonElement?> DeletePlaylistAsync(string listId)
    {
        var (uid, token) = GetAuth();

        return await rawApi.DeletePlaylistAsync(uid, token, listId);
    }

    /// <summary>
    ///     对歌单添加歌曲
    ///     <para>对应: /playlist/tracks/add</para>
    /// </summary>
    /// <param name="targetListId">目标歌单 ListID</param>
    /// <param name="songs">要添加的歌曲列表 (Name, Hash, AlbumId, MixSongId)</param>
    public async Task<JsonElement?> AddSongsAsync(
        string targetListId,
        List<(string Name, string Hash, string AlbumId, string MixSongId)> songs)
    {
        var (uid, token) = GetAuth();

        if (songs == null || songs.Count == 0) return null;

        return await rawApi.AddSongsToPlaylistAsync(uid, token, targetListId, songs);
    }

    /// <summary>
    ///     对歌单删除歌曲
    ///     <para>对应: /playlist/tracks/del</para>
    /// </summary>
    /// <param name="targetListId">目标歌单 ListID</param>
    /// <param name="fileIds">要删除的 FileID 列表 (注意: 这是歌单内的唯一ID，不是歌曲Hash)</param>
    public async Task<JsonElement?> RemoveSongsAsync(string targetListId, IEnumerable<long> fileIds)
    {
        var (uid, token) = GetAuth();

        // 简单的参数校验
        var ids = fileIds.ToList();
        if (ids.Count == 0) return null;

        return await rawApi.RemoveSongsFromPlaylistAsync(uid, token, targetListId, ids);
    }
}