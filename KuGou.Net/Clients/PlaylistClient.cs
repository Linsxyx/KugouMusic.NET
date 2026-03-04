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
    ///     收藏歌单
    /// </summary>
    /// <param name="name">歌单名称</param>
    /// <param name="sourceGlobalId">原歌单 GlobalID</param>
    public async Task<JsonElement?> CollectPlaylistAsync(
        string name,
        string sourceGlobalId)
    {
        var (uid, token) = GetAuth();

        // 调用 RawApi
        return await rawApi.CollectPlaylistAsync(uid, token, uid, "1", sourceGlobalId, name, 1,0);
    }
    
    /// <summary>
    ///    新建歌单
    /// </summary>
    /// <param name="name">歌单名称</param>
    /// <param name="isPri">是否设为隐私，0：公开，1：隐私，仅支持创建歌单时传入</param>
    public async Task<JsonElement?> CreatePlaylistAsync(
        string name,
        long isPri =0)
    {
        var (uid, token) = GetAuth();
        
        return await rawApi.CollectPlaylistAsync(uid, token, uid, "1", "", name, 0,isPri);
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
    public async Task<AddSongResponse?> AddSongsAsync(
        string targetListId,
        List<(string Name, string Hash, string AlbumId, string MixSongId)> songs)
    {
        var (uid, token) = GetAuth();

        if (songs == null || songs.Count == 0) return null;

        var json = await rawApi.AddSongsToPlaylistAsync(uid, token, targetListId, songs);
        return KgApiResponseParser.Parse<AddSongResponse>(json, AppJsonContext.Default.AddSongResponse);
    }

    /// <summary>
    ///     对歌单删除歌曲
    /// </summary>
    /// <param name="targetListId">目标歌单 ListID</param>
    /// <param name="fileIds">要删除的 FileID 列表 (注意: 这是歌单内的唯一ID，不是歌曲Hash)</param>
    public async Task<RemoveSongResponse?> RemoveSongsAsync(string targetListId, IEnumerable<long> fileIds)
    {
        var (uid, token) = GetAuth();

        var ids = fileIds.ToList();
        if (ids.Count == 0) return null;

        var json = await rawApi.RemoveSongsFromPlaylistAsync(uid, token, targetListId, ids);
        return KgApiResponseParser.Parse<RemoveSongResponse>(json, AppJsonContext.Default.RemoveSongResponse);
    }
}