using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.ComponentModel.DataAnnotations;

namespace KgWebApi.Net.Controllers;

[ApiController]
public class MediaCatalogController(
    VideoClient videoClient,
    LongAudioClient longAudioClient,
    IpClient ipClient,
    SceneClient sceneClient,
    ThemeClient themeClient) : ControllerBase
{
    /// <summary>
    ///     获取视频详情。
    /// </summary>
    /// <param name="id">视频 ID。</param>
    /// <returns>视频详情。</returns>
    [HttpGet("video/detail")]
    public async Task<IActionResult> GetVideoDetail([FromQuery][Required(AllowEmptyStrings = false)] string id)
    {
        return Ok(await videoClient.GetDetailAsync(id));
    }

    /// <summary>
    ///     获取视频 URL。
    /// </summary>
    /// <param name="hash">视频 hash。</param>
    /// <returns>视频 URL。</returns>
    [HttpGet("video/url")]
    public async Task<IActionResult> GetVideoUrl([FromQuery][Required(AllowEmptyStrings = false)] string hash)
    {
        return Ok(await videoClient.GetUrlAsync(hash));
    }

    /// <summary>
    ///     听书 - 专辑详情。
    /// </summary>
    /// <param name="albumId">专辑 ID。</param>
    /// <returns>听书专辑详情。</returns>
    [HttpGet("longaudio/album/detail")]
    public async Task<IActionResult> GetLongAudioAlbumDetail([FromQuery(Name = "album_id")][Required(AllowEmptyStrings = false)] string albumId)
    {
        return Ok(await longAudioClient.GetAlbumDetailAsync(albumId));
    }

    /// <summary>
    ///     听书 - 专辑音乐列表。
    /// </summary>
    /// <param name="albumId">专辑 ID。</param>
    /// <param name="page">页码。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <returns>听书专辑音乐列表。</returns>
    [HttpGet("longaudio/album/audios")]
    public async Task<IActionResult> GetLongAudioAlbumAudios(
        [FromQuery(Name = "album_id")][Required(AllowEmptyStrings = false)] string albumId,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await longAudioClient.GetAlbumAudiosAsync(albumId, page, pagesize));
    }

    /// <summary>
    ///     听书 - 每日推荐。
    /// </summary>
    /// <param name="page">页数。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <returns>听书每日推荐列表。</returns>
    [HttpGet("longaudio/daily/recommend")]
    public async Task<IActionResult> GetLongAudioDailyRecommend([FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await longAudioClient.GetDailyRecommendAsync(page, pagesize));
    }

    /// <summary>
    ///     听书 - 排行榜推荐。
    /// </summary>
    /// <returns>听书排行榜推荐。</returns>
    [HttpGet("longaudio/rank/recommend")]
    public async Task<IActionResult> GetLongAudioRankRecommend()
    {
        return Ok(await longAudioClient.GetRankRecommendAsync());
    }

    /// <summary>
    ///     听书 - VIP 推荐。
    /// </summary>
    /// <returns>听书 VIP 推荐。</returns>
    [HttpGet("longaudio/vip/recommend")]
    public async Task<IActionResult> GetLongAudioVipRecommend()
    {
        return Ok(await longAudioClient.GetVipRecommendAsync());
    }

    /// <summary>
    ///     听书 - 每周推荐。
    /// </summary>
    /// <returns>听书每周推荐。</returns>
    [HttpGet("longaudio/week/recommend")]
    public async Task<IActionResult> GetLongAudioWeekRecommend()
    {
        return Ok(await longAudioClient.GetWeekRecommendAsync());
    }

    /// <summary>
    ///     编辑精选数据。
    /// </summary>
    /// <param name="id">ip id。</param>
    /// <param name="type">数据类型。</param>
    /// <param name="page">页码。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <returns>编辑精选对应数据。</returns>
    [HttpGet("ip")]
    public async Task<IActionResult> GetIpResources(
        [FromQuery][Required(AllowEmptyStrings = false)] string id,
        [FromQuery] string type = "audios",
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await ipClient.GetResourcesAsync(id, type, page, pagesize));
    }

    /// <summary>
    ///     编辑精选详情。
    /// </summary>
    /// <param name="id">ip id。</param>
    /// <returns>编辑精选详情。</returns>
    [HttpGet("ip/detail")]
    public async Task<IActionResult> GetIpDetail([FromQuery][Required(AllowEmptyStrings = false)] string id)
    {
        return Ok(await ipClient.GetDetailAsync(id));
    }

    /// <summary>
    ///     编辑精选歌单。
    /// </summary>
    /// <param name="id">ip id。</param>
    /// <param name="page">页码。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <returns>编辑精选歌单数据。</returns>
    [HttpGet("ip/playlist")]
    public async Task<IActionResult> GetIpPlaylists(
        [FromQuery][Required(AllowEmptyStrings = false)] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await ipClient.GetPlaylistsAsync(id, page, pagesize));
    }

    /// <summary>
    ///     编辑精选专区。
    /// </summary>
    /// <returns>编辑精选专区列表。</returns>
    [HttpGet("ip/zone")]
    public async Task<IActionResult> GetIpZone()
    {
        return Ok(await ipClient.GetZoneAsync());
    }

    /// <summary>
    ///     编辑精选专区详情。
    /// </summary>
    /// <param name="id">ip id。</param>
    /// <returns>编辑精选专区详情。</returns>
    [HttpGet("ip/zone/home")]
    public async Task<IActionResult> GetIpZoneHome([FromQuery][Required(AllowEmptyStrings = false)] string id)
    {
        return Ok(await ipClient.GetZoneHomeAsync(id));
    }

    /// <summary>
    ///     场景音乐列表。
    /// </summary>
    /// <returns>场景音乐列表。</returns>
    [HttpGet("scene/lists")]
    public async Task<IActionResult> GetSceneLists()
    {
        return Ok(await sceneClient.GetListsAsync());
    }

    /// <summary>
    ///     获取场景音乐音乐列表。
    /// </summary>
    /// <param name="id">场景音乐 scene_id。</param>
    /// <param name="moduleId">场景音乐 module_id。</param>
    /// <param name="tag">场景音乐 tag_id。</param>
    /// <param name="page">页码。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <returns>场景音乐音乐列表。</returns>
    [HttpGet("scene/audio/list")]
    public async Task<IActionResult> GetSceneAudios(
        [FromQuery][Required(AllowEmptyStrings = false)] string id,
        [FromQuery(Name = "module_id")][Required(AllowEmptyStrings = false)] string moduleId,
        [FromQuery][Required(AllowEmptyStrings = false)] string tag,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await sceneClient.GetAudiosAsync(id, moduleId, tag, page, pagesize));
    }

    /// <summary>
    ///     获取场景音乐歌单列表。
    /// </summary>
    /// <param name="tagId">场景音乐 tag_id。</param>
    /// <param name="page">页码。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <returns>场景音乐歌单列表。</returns>
    [HttpGet("scene/collection/list")]
    public async Task<IActionResult> GetSceneCollections(
        [FromQuery(Name = "tag_id")][Required(AllowEmptyStrings = false)] string tagId,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await sceneClient.GetCollectionsAsync(tagId, page, pagesize));
    }

    /// <summary>
    ///     获取场景音乐讨论区。
    /// </summary>
    /// <param name="id">场景音乐 scene_id。</param>
    /// <param name="page">页码。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <param name="sort">排序，rec、hot、new。</param>
    /// <returns>场景音乐讨论区。</returns>
    [HttpGet("scene/lists/v2")]
    public async Task<IActionResult> GetSceneListsV2(
        [FromQuery][Required(AllowEmptyStrings = false)] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30,
        [FromQuery] string sort = "rec")
    {
        return Ok(await sceneClient.GetListsV2Async(id, page, pagesize, sort));
    }

    /// <summary>
    ///     场景音乐详情。
    /// </summary>
    /// <param name="id">场景音乐 scene_id。</param>
    /// <returns>场景音乐详情。</returns>
    [HttpGet("scene/module")]
    public async Task<IActionResult> GetSceneModules([FromQuery][Required(AllowEmptyStrings = false)] string id)
    {
        return Ok(await sceneClient.GetModulesAsync(id));
    }

    /// <summary>
    ///     获取场景音乐模块 Tag。
    /// </summary>
    /// <param name="id">场景音乐 scene_id。</param>
    /// <param name="moduleId">场景音乐 module_id。</param>
    /// <returns>场景模块 Tag 信息。</returns>
    [HttpGet("scene/module/info")]
    public async Task<IActionResult> GetSceneModuleInfo(
        [FromQuery][Required(AllowEmptyStrings = false)] string id,
        [FromQuery(Name = "module_id")][Required(AllowEmptyStrings = false)] string moduleId)
    {
        return Ok(await sceneClient.GetModuleInfoAsync(id, moduleId));
    }

    /// <summary>
    ///     场景音乐资源列表。
    /// </summary>
    /// <param name="id">场景音乐 scene_id。</param>
    /// <param name="page">页码。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <returns>场景音乐资源列表。</returns>
    [HttpGet("scene/music")]
    public async Task<IActionResult> GetSceneMusic(
        [FromQuery] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await sceneClient.GetMusicAsync(id, page, pagesize));
    }

    /// <summary>
    ///     获取场景音乐视频列表。
    /// </summary>
    /// <param name="tagId">场景音乐视频 tag_id。</param>
    /// <param name="page">页码。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <returns>场景音乐视频列表。</returns>
    [HttpGet("scene/video/list")]
    public async Task<IActionResult> GetSceneVideos(
        [FromQuery(Name = "tag_id")][Required(AllowEmptyStrings = false)] string tagId,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await sceneClient.GetVideosAsync(tagId, page, pagesize));
    }

    /// <summary>
    ///     获取主题音乐。
    /// </summary>
    /// <param name="ids">主题音乐 id。</param>
    /// <returns>主题音乐列表。</returns>
    [HttpGet("theme/music")]
    public async Task<IActionResult> GetThemeMusic([FromQuery][Required(AllowEmptyStrings = false)] string ids)
    {
        return Ok(await themeClient.GetMusicAsync(ids));
    }

    /// <summary>
    ///     主题歌单。
    /// </summary>
    /// <returns>主题歌单列表。</returns>
    [HttpGet("theme/playlist")]
    public async Task<IActionResult> GetThemePlaylists()
    {
        return Ok(await themeClient.GetPlaylistsAsync());
    }

    /// <summary>
    ///     获取主题音乐详情。
    /// </summary>
    /// <param name="id">主题音乐 id。</param>
    /// <returns>主题音乐详情。</returns>
    [HttpGet("theme/music/detail")]
    public async Task<IActionResult> GetThemeMusicDetail([FromQuery][Required(AllowEmptyStrings = false)] string id)
    {
        return Ok(await themeClient.GetMusicDetailAsync(id));
    }

    /// <summary>
    ///     获取主题歌单所有歌曲。
    /// </summary>
    /// <param name="themeId">主题歌单 id。</param>
    /// <returns>主题歌单歌曲列表。</returns>
    [HttpGet("theme/playlist/track")]
    public async Task<IActionResult> GetThemePlaylistTracks([FromQuery(Name = "theme_id")][Required(AllowEmptyStrings = false)] string themeId)
    {
        return Ok(await themeClient.GetPlaylistTracksAsync(themeId));
    }
}
