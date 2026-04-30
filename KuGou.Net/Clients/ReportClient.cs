using System.Text.Json;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using KuGou.Net.util;

namespace KuGou.Net.Clients;

public class ReportClient(RawReportApi rawApi, KgSessionManager sessionManager)
{
    private bool IsLoggedIn()
    {
        var session = sessionManager.Session;
        return !string.IsNullOrWhiteSpace(session.Token) && session.UserId != "0";
    }

    public async Task<JsonElement?> UploadPlayHistoryAsync(long mixSongId, long? timestamp = null, int playCount = 1)
    {
        if (!IsLoggedIn()) return null;
        var session = sessionManager.Session;
        return await rawApi.UploadPlayHistoryAsync(session.UserId, session.Token, mixSongId, timestamp, playCount);
    }

    public async Task<JsonElement?> GetLatestSongsAsync(int pageSize = 30)
    {
        if (!IsLoggedIn()) return null;
        var session = sessionManager.Session;
        return await rawApi.GetLatestSongsAsync(session.UserId, session.Token, pageSize);
    }

    public async Task<JsonElement?> AddListenTimeAsync()
    {
        if (!IsLoggedIn()) return null;
        var session = sessionManager.Session;
        var mid = string.IsNullOrWhiteSpace(session.Mid) || session.Mid == "-"
            ? KgUtils.CalcNewMid(session.Dfid)
            : session.Mid;
        return await rawApi.AddListenTimeAsync(session.UserId, session.Token, mid);
    }
}
