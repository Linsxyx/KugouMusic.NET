using KuGou.Net.Abstractions.Models;
using KuGou.Net.Adapters.Common;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.util;
using System.Text.Json;

namespace KuGou.Net.Clients;

public class AlbumClient(RawAlbumApi rawApi)
{
    public Task<JsonElement> GetAlbumShopAsync()
    {
        return rawApi.GetAlbumShopAsync();
    }

    public async Task<List<AlbumSongItem>?> GetSongsAsync(string albumId, int page = 1, int pageSize = 30)
    {
        var response = await GetAlbumSongsPageAsync(albumId, page, pageSize);
        return response.Songs;
    }

    /// <summary>
    ///     拉取专辑的一页歌曲，并保留服务端声明的总歌曲数（用于判断是否还有下一页）。
    /// </summary>
    public async Task<AlbumSongResponse> GetAlbumSongsPageAsync(string albumId, int page = 1, int pageSize = 30)
    {
        var json = await rawApi.GetAlbumSongAsync(albumId, page, pageSize);

        return KgApiResponseParser.Parse<AlbumSongResponse>(json, AppJsonContext.Default.AlbumSongResponse)
               ?? new AlbumSongResponse();
    }

    public Task<JsonElement> GetAlbumRawAsync(string albumIds, string? fields = null)
    {
        return rawApi.GetAlbumAsync(albumIds, fields);
    }

    public Task<JsonElement> GetDetailRawAsync(string albumId)
    {
        return rawApi.GetAlbumInfoAsync(albumId);
    }

    public Task<JsonElement> GetSongsRawAsync(string albumId, int page = 1, int pageSize = 30)
    {
        return rawApi.GetAlbumSongAsync(albumId, page, pageSize);
    }
}
