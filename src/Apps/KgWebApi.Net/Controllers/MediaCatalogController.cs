using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
public class MediaCatalogController(
    VideoClient videoClient,
    LongAudioClient longAudioClient,
    IpClient ipClient,
    SceneClient sceneClient,
    ThemeClient themeClient) : ControllerBase
{
    [HttpGet("video/detail")]
    public async Task<IActionResult> GetVideoDetail([FromQuery] string id)
    {
        return Ok(await videoClient.GetDetailAsync(id));
    }

    [HttpGet("video/url")]
    public async Task<IActionResult> GetVideoUrl([FromQuery] string hash)
    {
        return Ok(await videoClient.GetUrlAsync(hash));
    }

    [HttpGet("longaudio/album/detail")]
    public async Task<IActionResult> GetLongAudioAlbumDetail([FromQuery(Name = "album_id")] string albumId)
    {
        return Ok(await longAudioClient.GetAlbumDetailAsync(albumId));
    }

    [HttpGet("longaudio/album/audios")]
    public async Task<IActionResult> GetLongAudioAlbumAudios(
        [FromQuery(Name = "album_id")] string albumId,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await longAudioClient.GetAlbumAudiosAsync(albumId, page, pagesize));
    }

    [HttpGet("longaudio/daily/recommend")]
    public async Task<IActionResult> GetLongAudioDailyRecommend([FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await longAudioClient.GetDailyRecommendAsync(page, pagesize));
    }

    [HttpGet("longaudio/rank/recommend")]
    public async Task<IActionResult> GetLongAudioRankRecommend()
    {
        return Ok(await longAudioClient.GetRankRecommendAsync());
    }

    [HttpGet("longaudio/vip/recommend")]
    public async Task<IActionResult> GetLongAudioVipRecommend()
    {
        return Ok(await longAudioClient.GetVipRecommendAsync());
    }

    [HttpGet("longaudio/week/recommend")]
    public async Task<IActionResult> GetLongAudioWeekRecommend()
    {
        return Ok(await longAudioClient.GetWeekRecommendAsync());
    }

    [HttpGet("ip")]
    public async Task<IActionResult> GetIpResources(
        [FromQuery] string id,
        [FromQuery] string type = "audios",
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await ipClient.GetResourcesAsync(id, type, page, pagesize));
    }

    [HttpGet("ip/detail")]
    public async Task<IActionResult> GetIpDetail([FromQuery] string id)
    {
        return Ok(await ipClient.GetDetailAsync(id));
    }

    [HttpGet("ip/playlist")]
    public async Task<IActionResult> GetIpPlaylists(
        [FromQuery] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await ipClient.GetPlaylistsAsync(id, page, pagesize));
    }

    [HttpGet("ip/zone")]
    public async Task<IActionResult> GetIpZone()
    {
        return Ok(await ipClient.GetZoneAsync());
    }

    [HttpGet("ip/zone/home")]
    public async Task<IActionResult> GetIpZoneHome([FromQuery] string id)
    {
        return Ok(await ipClient.GetZoneHomeAsync(id));
    }

    [HttpGet("scene/lists")]
    public async Task<IActionResult> GetSceneLists()
    {
        return Ok(await sceneClient.GetListsAsync());
    }

    [HttpGet("scene/audio/list")]
    public async Task<IActionResult> GetSceneAudios(
        [FromQuery] string id,
        [FromQuery(Name = "module_id")] string? moduleId = null,
        [FromQuery] string? tag = null,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await sceneClient.GetAudiosAsync(id, moduleId, tag, page, pagesize));
    }

    [HttpGet("scene/collection/list")]
    public async Task<IActionResult> GetSceneCollections(
        [FromQuery(Name = "tag_id")] string tagId,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await sceneClient.GetCollectionsAsync(tagId, page, pagesize));
    }

    [HttpGet("scene/lists/v2")]
    public async Task<IActionResult> GetSceneListsV2(
        [FromQuery] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30,
        [FromQuery] string sort = "rec")
    {
        return Ok(await sceneClient.GetListsV2Async(id, page, pagesize, sort));
    }

    [HttpGet("scene/module")]
    public async Task<IActionResult> GetSceneModules([FromQuery] string id)
    {
        return Ok(await sceneClient.GetModulesAsync(id));
    }

    [HttpGet("scene/module/info")]
    public async Task<IActionResult> GetSceneModuleInfo(
        [FromQuery] string id,
        [FromQuery(Name = "module_id")] string moduleId)
    {
        return Ok(await sceneClient.GetModuleInfoAsync(id, moduleId));
    }

    [HttpGet("scene/music")]
    public async Task<IActionResult> GetSceneMusic(
        [FromQuery] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await sceneClient.GetMusicAsync(id, page, pagesize));
    }

    [HttpGet("scene/video/list")]
    public async Task<IActionResult> GetSceneVideos(
        [FromQuery(Name = "tag_id")] string tagId,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await sceneClient.GetVideosAsync(tagId, page, pagesize));
    }

    [HttpGet("theme/music")]
    public async Task<IActionResult> GetThemeMusic([FromQuery] string ids)
    {
        return Ok(await themeClient.GetMusicAsync(ids));
    }

    [HttpGet("theme/playlist")]
    public async Task<IActionResult> GetThemePlaylists()
    {
        return Ok(await themeClient.GetPlaylistsAsync());
    }

    [HttpGet("theme/music/detail")]
    public async Task<IActionResult> GetThemeMusicDetail([FromQuery] string id)
    {
        return Ok(await themeClient.GetMusicDetailAsync(id));
    }

    [HttpGet("theme/playlist/track")]
    public async Task<IActionResult> GetThemePlaylistTracks([FromQuery(Name = "theme_id")] string themeId)
    {
        return Ok(await themeClient.GetPlaylistTracksAsync(themeId));
    }
}
