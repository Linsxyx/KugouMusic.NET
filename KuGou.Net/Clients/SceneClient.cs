using System.Text.Json;
using KuGou.Net.Protocol.Raw;

namespace KuGou.Net.Clients;

public class SceneClient(RawMediaCatalogApi rawApi)
{
    public Task<JsonElement> GetListsAsync()
    {
        return rawApi.GetSceneListsAsync();
    }

    public Task<JsonElement> GetAudiosAsync(string sceneId, string? moduleId = null, string? tag = null,
        int page = 1, int pageSize = 30)
    {
        return rawApi.GetSceneAudiosAsync(sceneId, moduleId, tag, page, pageSize);
    }

    public Task<JsonElement> GetCollectionsAsync(string tagId, int page = 1, int pageSize = 30)
    {
        return rawApi.GetSceneCollectionsAsync(tagId, page, pageSize);
    }

    public Task<JsonElement> GetListsV2Async(string sceneId, int page = 1, int pageSize = 30, string sort = "rec")
    {
        return rawApi.GetSceneListsV2Async(sceneId, page, pageSize, sort);
    }

    public Task<JsonElement> GetModulesAsync(string sceneId)
    {
        return rawApi.GetSceneModulesAsync(sceneId);
    }

    public Task<JsonElement> GetModuleInfoAsync(string sceneId, string moduleId)
    {
        return rawApi.GetSceneModuleInfoAsync(sceneId, moduleId);
    }

    public Task<JsonElement> GetMusicAsync(string sceneId, int page = 1, int pageSize = 30)
    {
        return rawApi.GetSceneMusicAsync(sceneId, page, pageSize);
    }

    public Task<JsonElement> GetVideosAsync(string tagId, int page = 1, int pageSize = 30)
    {
        return rawApi.GetSceneVideosAsync(tagId, page, pageSize);
    }
}
