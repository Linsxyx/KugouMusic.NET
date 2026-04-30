using System.Text.Json;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.util;

namespace KuGou.Net.Clients;

public class SongClient(RawSongApi rawApi, RawSearchApi rawSearchApi)
{
    public async Task<PlayUrlData?> GetPlayInfoAsync(string hash, string? quality = null)
    {
        var json = await rawSearchApi.GetPlayUrlAsync(hash, quality);
        var result = json.Deserialize(AppJsonContext.Default.PlayUrlData);
        return result ?? new PlayUrlData { Status = 0 };
    }

    public Task<JsonElement> GetUrlNewAsync(string hash, string? albumAudioId = null, bool freePart = false)
    {
        return rawApi.GetUrlNewAsync(hash, albumAudioId, freePart);
    }

    public Task<JsonElement> GetUrlAsync(string hash, string? quality = "128", string? albumId = null,
        string? albumAudioId = null, bool freePart = false)
    {
        return rawApi.GetUrlAsync(hash, quality, albumId, albumAudioId, freePart);
    }

    public Task<JsonElement> GetVideoUrlAsync(string hash)
    {
        return rawApi.GetVideoUrlAsync(hash);
    }
}
