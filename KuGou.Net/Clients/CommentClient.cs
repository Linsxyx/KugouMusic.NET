using System.Text.Json;
using KuGou.Net.Protocol.Raw;

namespace KuGou.Net.Clients;

public class CommentClient(RawCommentApi rawApi)
{
    public Task<JsonElement> GetMusicCommentsAsync(string mixSongId, int page = 1, int pageSize = 30)
    {
        return rawApi.GetMusicCommentsAsync(mixSongId, page, pageSize);
    }

    public Task<JsonElement> GetPlaylistCommentsAsync(string id, int page = 1, int pageSize = 30)
    {
        return rawApi.GetPlaylistCommentsAsync(id, page, pageSize);
    }

    public Task<JsonElement> GetAlbumCommentsAsync(string id, int page = 1, int pageSize = 30)
    {
        return rawApi.GetAlbumCommentsAsync(id, page, pageSize);
    }

    public Task<JsonElement> GetCommentCountAsync(string? hash = null, string? specialId = null)
    {
        return rawApi.GetCommentCountAsync(hash, specialId);
    }

    public Task<JsonElement> GetFloorCommentsAsync(
        string? specialId,
        string tid,
        string? mixSongId = null,
        string resourceType = "song",
        int page = 1,
        int pageSize = 30,
        int showClassify = 1,
        int showHotwordList = 1,
        string? code = null)
    {
        return rawApi.GetFloorCommentsAsync(
            specialId,
            tid,
            mixSongId,
            resourceType,
            page,
            pageSize,
            showClassify,
            showHotwordList,
            code);
    }

    public Task<JsonElement> GetMusicCommentClassifyAsync(string mixSongId, string typeId, int page = 1,
        int pageSize = 30, int sort = 1)
    {
        return rawApi.GetMusicCommentClassifyAsync(mixSongId, typeId, page, pageSize, sort);
    }

    public Task<JsonElement> GetMusicCommentHotwordAsync(string mixSongId, string hotWord, int page = 1,
        int pageSize = 30)
    {
        return rawApi.GetMusicCommentHotwordAsync(mixSongId, hotWord, page, pageSize);
    }
}
