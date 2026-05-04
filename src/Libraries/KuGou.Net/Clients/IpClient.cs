using System.Text.Json;
using KuGou.Net.Protocol.Raw;

namespace KuGou.Net.Clients;

public class IpClient(RawMediaCatalogApi rawApi)
{
    public Task<JsonElement> GetResourcesAsync(string id, string type = "audios", int page = 1, int pageSize = 30)
    {
        return rawApi.GetIpResourcesAsync(id, type, page, pageSize);
    }

    public Task<JsonElement> GetDetailAsync(string ids)
    {
        return rawApi.GetIpDetailAsync(ids);
    }

    public Task<JsonElement> GetPlaylistsAsync(string id, int page = 1, int pageSize = 30)
    {
        return rawApi.GetIpPlaylistsAsync(id, page, pageSize);
    }

    public Task<JsonElement> GetZoneAsync()
    {
        return rawApi.GetIpZoneAsync();
    }

    public Task<JsonElement> GetZoneHomeAsync(string id)
    {
        return rawApi.GetIpZoneHomeAsync(id);
    }
}
