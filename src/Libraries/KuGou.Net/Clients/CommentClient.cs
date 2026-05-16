using System.Text.Json;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Adapters.Common;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.util;

namespace KuGou.Net.Clients;

public class CommentClient(RawCommentApi rawApi)
{
    public async Task<MusicCommentResponse?> GetMusicCommentsAsync(string mixSongId, int page = 1, int pageSize = 30)
    {
        var json = await rawApi.GetMusicCommentsAsync(mixSongId, page, pageSize);
        return KgApiResponseParser.Parse<MusicCommentResponse>(json, AppJsonContext.Default.MusicCommentResponse);
    }

    public async Task<MusicCommentResponse?> GetPlaylistCommentsAsync(string id, int page = 1, int pageSize = 30)
    {
        var json = await rawApi.GetPlaylistCommentsAsync(id, page, pageSize);
        return KgApiResponseParser.Parse<MusicCommentResponse>(json, AppJsonContext.Default.MusicCommentResponse);
    }

    public async Task<MusicCommentResponse?> GetAlbumCommentsAsync(string id, int page = 1, int pageSize = 30)
    {
        var json = await rawApi.GetAlbumCommentsAsync(id, page, pageSize);
        return KgApiResponseParser.Parse<MusicCommentResponse>(json, AppJsonContext.Default.MusicCommentResponse);
    }

    public async Task<Dictionary<string, int>?> GetCommentCountAsync(string? hash = null, string? specialId = null)
    {
        var json = await rawApi.GetCommentCountAsync(hash, specialId);
        
        return KgApiResponseParser.Parse<Dictionary<string, int>>(
            json, 
            AppJsonContext.Default.DictionaryStringInt32
        );
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
