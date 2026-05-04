using System.Text.Json;
using KuGou.Net.Protocol.Raw;

namespace KuGou.Net.Clients;

public class ThemeClient(RawMediaCatalogApi rawApi)
{
    public Task<JsonElement> GetMusicAsync(string ids)
    {
        return rawApi.GetThemeMusicAsync(ids);
    }

    public Task<JsonElement> GetPlaylistsAsync()
    {
        return rawApi.GetThemePlaylistsAsync();
    }

    public Task<JsonElement> GetMusicDetailAsync(string id)
    {
        return rawApi.GetThemeMusicDetailAsync(id);
    }

    public Task<JsonElement> GetPlaylistTracksAsync(string themeId)
    {
        return rawApi.GetThemePlaylistTracksAsync(themeId);
    }
}
