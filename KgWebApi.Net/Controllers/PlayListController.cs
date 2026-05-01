using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("playlist")]
public class PlayListController(PlaylistClient playlistClient) : ControllerBase
{
    [HttpGet("/sheet/collection")]
    public async Task<IActionResult> GetSheetCollection([FromQuery] int position = 2)
    {
        return Ok(await playlistClient.GetSheetCollectionAsync(position));
    }

    [HttpGet("/sheet/collection/detail")]
    public async Task<IActionResult> GetSheetCollectionDetail(
        [FromQuery(Name = "collection_id")] string collectionId,
        [FromQuery] int page = 1)
    {
        return Ok(await playlistClient.GetSheetCollectionDetailAsync(collectionId, page));
    }

    [HttpGet("/sheet/detail")]
    public async Task<IActionResult> GetSheetDetail(
        [FromQuery] string id,
        [FromQuery] string source)
    {
        return Ok(await playlistClient.GetSheetDetailAsync(id, source));
    }

    [HttpGet("/sheet/hot")]
    public async Task<IActionResult> GetSheetHot([FromQuery(Name = "opern_type")] int opernType = 1)
    {
        return Ok(await playlistClient.GetSheetHotAsync(opernType));
    }

    [HttpGet("/sheet/list")]
    public async Task<IActionResult> GetSheetList(
        [FromQuery(Name = "album_audio_id")] string albumAudioId,
        [FromQuery(Name = "opern_type")] int opernType = 0,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await playlistClient.GetSheetListAsync(albumAudioId, opernType, page, pagesize));
    }

    [HttpGet("detail")]
    public async Task<IActionResult> GetDetail([FromQuery(Name = "ids")] string ids)
    {
        var result = await playlistClient.GetInfoAsync(ids);
        return Ok(result);
    }

    [HttpGet("tags")]
    public async Task<IActionResult> GetTags()
    {
        var result = await playlistClient.GetTagsAsync();

        if (result == null) return NotFound(new { status = 0, msg = "未获取到标签数据" });

        return Ok(result);
    }

    [HttpGet("track/all")]
    public async Task<IActionResult> GetDtrackAll(
        [FromQuery] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        var result = await playlistClient.GetSongsAsync(id, page, pagesize);
        return Ok(result);
    }

    [HttpGet("track/all/new")]
    public async Task<IActionResult> GetTrackAllNew(
        [FromQuery] string listid,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        var result = await playlistClient.GetSongsNewRawAsync(listid, page, pagesize);
        return Ok(result);
    }

    [HttpGet("similar")]
    public async Task<IActionResult> GetSimilar([FromQuery] string ids)
    {
        var result = await playlistClient.GetSimilarRawAsync(ids);
        return Ok(result);
    }

    [HttpGet("effect")]
    public async Task<IActionResult> GetEffect([FromQuery] int page = 1, [FromQuery] int pagesize = 30)
    {
        var result = await playlistClient.GetSoundEffectRawAsync(page, pagesize);
        return Ok(result);
    }

    /// <summary>
    ///     收藏歌单
    /// </summary>
    [HttpPost("add")]
    public async Task<IActionResult> AddPlaylist(
        [FromQuery] string name,
        [FromQuery(Name = "list_create_gid")] string sourceGlobalId)
    {
        var result = await playlistClient.CollectPlaylistAsync(name, sourceGlobalId);
        return Ok(result);
    }

    /// <summary>
    ///     新建歌单
    /// </summary>
    [HttpPost("create")]
    public async Task<IActionResult> CreatePlaylist(
        [FromQuery] string name,
        [FromQuery(Name = "type")] long type = 0)
    {
        var result = await playlistClient.CreatePlaylistAsync(name, type);
        return Ok(result);
    }

    /// <summary>
    ///     取消收藏 / 删除歌单
    ///     路由: POST /PlayList/del?listid=xxx
    /// </summary>
    [HttpPost("del")]
    public async Task<IActionResult> DeletePlaylist([FromQuery] string listid)
    {
        var result = await playlistClient.DeletePlaylistAsync(listid);
        return Ok(result);
    }

    /// <summary>
    ///     对歌单添加歌曲
    ///     路由: POST /PlayList/tracks/add?listid=xx&data=歌名|Hash|AlbumId...
    /// </summary>
    [HttpPost("tracks/add")]
    public async Task<IActionResult> AddTracks([FromBody] AddTracksRequest request)
    {
        if (string.IsNullOrEmpty(request.ListId))
            return BadRequest("ListId 不能为空");

        if (request.Songs == null || request.Songs.Count == 0)
            return BadRequest("歌曲列表不能为空");

        // 转换 DTO 到 Tuple List
        var songList = request.Songs.Select(s => (
            s.Name,
            s.Hash,
            AlbumId: string.IsNullOrEmpty(s.AlbumId) ? "0" : s.AlbumId,
            MixSongId: string.IsNullOrEmpty(s.MixSongId) ? "0" : s.MixSongId
        )).ToList();

        var result = await playlistClient.AddSongsAsync(request.ListId, songList);
        return Ok(result);
    }

    /// <summary>
    ///     对歌单删除歌曲
    ///     路由: POST /PlayList/tracks/del?listid=xx&fileids=123,456
    /// </summary>
    [HttpPost("tracks/del")]
    public async Task<IActionResult> DeleteTracks([FromQuery] string listid, [FromQuery] string fileids)
    {
        if (string.IsNullOrEmpty(fileids)) return BadRequest("fileids cannot be empty");

        var idList = new List<long>();
        foreach (var idStr in fileids.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (long.TryParse(idStr, out var id))
                idList.Add(id);

        var result = await playlistClient.RemoveSongsAsync(listid, idList);
        return Ok(result);
    }
}

public record AddTracksRequest
{
    public string ListId { get; set; } = "";
    public List<AddSongItem> Songs { get; set; } = new();
}

public record AddSongItem
{
    public string Name { get; set; } = "";
    public string Hash { get; set; } = "";

    // 下面这两个是可选的，不传默认为 "0"
    public string? AlbumId { get; set; }
    public string? MixSongId { get; set; }
}

public record RemoveTracksRequest
{
    public string ListId { get; set; } = "";

    // 直接传数字数组，比逗号分隔的字符串舒服多了
    public List<long> FileIds { get; set; } = new();
}
