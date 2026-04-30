using KuGou.Net.Abstractions.Models;
using KuGou.Net.Adapters.Common;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.util;
using System.Text.Json;

namespace KuGou.Net.Clients;

public class AlbumClient(RawAlbumApi rawApi)
{
    public async Task<List<AlbumSongItem>?> GetSongsAsync(string albumId, int page = 1, int pageSize = 30)
    {
        var json = await rawApi.GetAlbumSongAsync(albumId, page, pageSize);

        var response = KgApiResponseParser.Parse<AlbumSongResponse>(json, AppJsonContext.Default.AlbumSongResponse);
        return response?.Songs;
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
