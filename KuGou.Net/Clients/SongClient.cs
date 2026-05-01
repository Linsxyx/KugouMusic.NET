using System.Text.Json;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.util;

namespace KuGou.Net.Clients;

public class SongClient(RawSongApi rawApi, RawSearchApi rawSearchApi)
{
    public Task<JsonElement> GetAudioAsync(string hash)
    {
        return rawApi.GetAudioAsync(hash);
    }

    public Task<JsonElement> GetAudioRelatedAsync(long albumAudioId, int page = 1, int pageSize = 30,
        string sort = "all", int type = 0, int showType = 0, bool showDetail = true)
    {
        return rawApi.GetAudioRelatedAsync(albumAudioId, page, pageSize, sort, type, showType, showDetail);
    }

    public Task<JsonElement> GetAudioAccompanyMatchingAsync(string hash, long mixId = 0, string? fileName = null)
    {
        return rawApi.GetAudioAccompanyMatchingAsync(hash, mixId, fileName);
    }

    public Task<JsonElement> GetAudioKtvTotalAsync(long songId, string songHash, string singerName)
    {
        return rawApi.GetAudioKtvTotalAsync(songId, songHash, singerName);
    }

    public Task<JsonElement> GetKmrAudioMvAsync(string albumAudioIds, string? fields = null)
    {
        return rawApi.GetKmrAudioMvAsync(albumAudioIds, fields);
    }

    public Task<JsonElement> GetKmrAudioAsync(string albumAudioIds, string? fields = "base")
    {
        return rawApi.GetKmrAudioAsync(albumAudioIds, fields);
    }

    public Task<JsonElement> GetSongClimaxAsync(string hash)
    {
        return rawApi.GetSongClimaxAsync(hash);
    }

    public Task<JsonElement> GetSongRankingAsync(string albumAudioId)
    {
        return rawApi.GetSongRankingAsync(albumAudioId);
    }

    public Task<JsonElement> GetSongRankingFilterAsync(string albumAudioId, int page = 1, int pageSize = 30)
    {
        return rawApi.GetSongRankingFilterAsync(albumAudioId, page, pageSize);
    }

    public Task<JsonElement> GetPrivilegeLiteAsync(string hash, string? albumIds = null)
    {
        return rawApi.GetPrivilegeLiteAsync(hash, albumIds);
    }

    public Task<JsonElement> GetImagesAsync(string hash, string? albumIds = null, string? albumAudioIds = null,
        int count = 5)
    {
        return rawApi.GetImagesAsync(hash, albumIds, albumAudioIds, count);
    }

    public Task<JsonElement> GetAudioImagesAsync(string hash, string? audioIds = null, string? albumAudioIds = null,
        string? fileNames = null, int count = 5)
    {
        return rawApi.GetAudioImagesAsync(hash, audioIds, albumAudioIds, fileNames, count);
    }

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
