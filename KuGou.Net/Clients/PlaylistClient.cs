using System.Text.Json;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Adapters.Common;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using KuGou.Net.util;

namespace KuGou.Net.Clients;

public class PlaylistClient(RawPlaylistApi rawApi, KgSessionManager sessionManager)
{
    public Task<JsonElement> GetSheetCollectionAsync(int position = 2)
    {
        return rawApi.GetSheetCollectionAsync(position);
    }

    public Task<JsonElement> GetSheetCollectionDetailAsync(string collectionId, int page = 1)
    {
        return rawApi.GetSheetCollectionDetailAsync(collectionId, page);
    }

    public Task<JsonElement> GetSheetDetailAsync(string id, string source)
    {
        return rawApi.GetSheetDetailAsync(id, source);
    }

    public Task<JsonElement> GetSheetHotAsync(int opernType = 1)
    {
        return rawApi.GetSheetHotAsync(opernType);
    }

    public Task<JsonElement> GetSheetListAsync(string albumAudioId, int opernType = 0, int page = 1, int pageSize = 30)
    {
        return rawApi.GetSheetListAsync(albumAudioId, opernType, page, pageSize);
    }

    /// <summary>
    ///     获取歌单内的歌曲列表
    /// </summary>
    public async Task<PlaylistSongResponse?> GetSongsAsync(string playlistId, int page = 1, int pageSize = 30)
    {
        // 逻辑搬运：计算起始索引
        var beginIdx = (page - 1) * pageSize;

        var json = await rawApi.GetPlaylistSongsAsync(playlistId, beginIdx, pageSize);
        var data = KgApiResponseParser.Parse<PlaylistSongResponse>(json, AppJsonContext.Default.PlaylistSongResponse);

        return data;
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


    public async Task<List<PlaylistTagCategory>?> GetTagsAsync()
    {
        var json = await rawApi.GetPlaylistTagsAsync();
        return KgApiResponseParser.Parse<List<PlaylistTagCategory>>(json,
            AppJsonContext.Default.ListPlaylistTagCategory);
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
        return await rawApi.CollectPlaylistAsync(uid, token, uid, "1", sourceGlobalId, name, 1, 1, 0);
    }

    /// <summary>
    ///     收藏专辑
    /// </summary>
    /// <param name="name">专辑名称</param>
    /// <param name="albumId">原专辑 ID</param>
    /// <param name="authorId">原专辑歌手 ID，未知时传 0</param>
    public async Task<JsonElement?> CollectAlbumAsync(
        string name,
        long albumId,
        long authorId = 0)
    {
        var (uid, token) = GetAuth();

        return await rawApi.CollectPlaylistAsync(
            uid,
            token,
            authorId > 0 ? authorId.ToString() : "0",
            albumId.ToString(),
            "",
            name,
            1,
            2,
            0);
    }

    /// <summary>
    ///     新建歌单
    /// </summary>
    /// <param name="name">歌单名称</param>
    /// <param name="isPri">是否设为隐私，0：公开，1：隐私，仅支持创建歌单时传入</param>
    public async Task<JsonElement?> CreatePlaylistAsync(
        string name,
        long isPri = 0)
    {
        var (uid, token) = GetAuth();

        return await rawApi.CollectPlaylistAsync(uid, token, uid, "1", "", name, 0, 1, isPri);
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

    public Task<JsonElement> GetSongsRawAsync(string playlistId, int page = 1, int pageSize = 30)
    {
        var begin = Math.Max(0, (page - 1) * pageSize);
        return rawApi.GetPlaylistSongsAsync(playlistId, begin, pageSize);
    }

    public Task<JsonElement> GetSongsNewRawAsync(string listId, int page = 1, int pageSize = 30)
    {
        var session = sessionManager.Session;
        return rawApi.GetPlaylistSongsNewAsync(listId, session.UserId, session.Token, page, pageSize);
    }

    public Task<JsonElement> GetSimilarRawAsync(string ids)
    {
        return rawApi.GetSimilarPlaylistsAsync(ids, sessionManager.Session.UserId);
    }

    public Task<JsonElement> GetSoundEffectRawAsync(int page = 1, int pageSize = 30)
    {
        return rawApi.GetSoundEffectPlaylistsAsync(page, pageSize);
    }
}
