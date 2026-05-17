using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("playlist")]
public class PlayListController(PlaylistClient playlistClient) : ControllerBase
{
    /// <summary>
    ///     推荐曲谱。
    /// </summary>
    /// <param name="position">曲谱位置。</param>
    /// <returns>推荐曲谱列表。</returns>
    [HttpGet("/sheet/collection")]
    public async Task<IActionResult> GetSheetCollection([FromQuery] int position = 2)
    {
        return Ok(await playlistClient.GetSheetCollectionAsync(position));
    }

    /// <summary>
    ///     曲谱合集详情。
    /// </summary>
    /// <param name="collectionId">曲谱合集 ID。</param>
    /// <param name="page">页码。</param>
    /// <returns>曲谱合集详情。</returns>
    [HttpGet("/sheet/collection/detail")]
    public async Task<IActionResult> GetSheetCollectionDetail(
        [FromQuery(Name = "collection_id")][Required(AllowEmptyStrings = false)] string collectionId,
        [FromQuery] int page = 1)
    {
        return Ok(await playlistClient.GetSheetCollectionDetailAsync(collectionId, page));
    }

    /// <summary>
    ///     曲谱详情。
    /// </summary>
    /// <param name="id">曲谱 ID。</param>
    /// <param name="source">曲谱 source。</param>
    /// <returns>曲谱详情。</returns>
    [HttpGet("/sheet/detail")]
    public async Task<IActionResult> GetSheetDetail(
        [FromQuery][Required(AllowEmptyStrings = false)] string id,
        [FromQuery][Required(AllowEmptyStrings = false)] string source)
    {
        return Ok(await playlistClient.GetSheetDetailAsync(id, source));
    }

    /// <summary>
    ///     曲谱合集。
    /// </summary>
    /// <param name="opernType">曲谱类型。</param>
    /// <returns>曲谱合集列表。</returns>
    [HttpGet("/sheet/hot")]
    public async Task<IActionResult> GetSheetHot([FromQuery(Name = "opern_type")] int opernType = 1)
    {
        return Ok(await playlistClient.GetSheetHotAsync(opernType));
    }

    /// <summary>
    ///     歌曲曲谱。
    /// </summary>
    /// <param name="albumAudioId">音乐的 mixsongid/album_audio_id。</param>
    /// <param name="opernType">曲谱类型。</param>
    /// <param name="page">页码。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <returns>歌曲曲谱列表。</returns>
    [HttpGet("/sheet/list")]
    public async Task<IActionResult> GetSheetList(
        [FromQuery(Name = "album_audio_id")][Required(AllowEmptyStrings = false)] string albumAudioId,
        [FromQuery(Name = "opern_type")] int opernType = 0,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await playlistClient.GetSheetListAsync(albumAudioId, opernType, page, pagesize));
    }

    /// <summary>
    ///     获取歌单详情。
    /// </summary>
    /// <param name="ids">歌单 ID。</param>
    /// <returns>歌单详情。</returns>
    [HttpGet("detail")]
    [ProducesResponseType(typeof(PlaylistInfo), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDetail([FromQuery(Name = "ids")][Required(AllowEmptyStrings = false)] string ids)
    {
        var result = await playlistClient.GetInfoAsync(ids);
        return Ok(result);
    }

    /// <summary>
    ///     获取歌单标签分类。
    /// </summary>
    /// <returns>歌单标签分类列表。</returns>
    [HttpGet("tags")]
    [ProducesResponseType(typeof(List<PlaylistTagCategory>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTags()
    {
        var result = await playlistClient.GetTagsAsync();

        if (result == null) return NotFound(new { status = 0, msg = "未获取到标签数据" });

        return Ok(result);
    }

    /// <summary>
    ///     获取歌单全部歌曲。
    /// </summary>
    /// <param name="id">歌单 ID。</param>
    /// <param name="page">页码。</param>
    /// <param name="pagesize">每页数量。</param>
    /// <returns>歌单歌曲分页结果。</returns>
    [HttpGet("track/all")]
    [ProducesResponseType(typeof(PlaylistSongResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDtrackAll(
        [FromQuery][Required(AllowEmptyStrings = false)] string id,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        var result = await playlistClient.GetSongsAsync(id, page, pagesize);
        return Ok(result);
    }

    [HttpGet("track/all/new")]
    public async Task<IActionResult> GetTrackAllNew(
        [FromQuery][Required(AllowEmptyStrings = false)] string listid,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        var result = await playlistClient.GetSongsNewRawAsync(listid, page, pagesize);
        return Ok(result);
    }

    /// <summary>
    ///     相似歌单。
    /// </summary>
    /// <param name="ids">歌单 global_collection_id。</param>
    /// <returns>相似歌单列表。</returns>
    [HttpGet("similar")]
    public async Task<IActionResult> GetSimilar([FromQuery][Required(AllowEmptyStrings = false)] string ids)
    {
        var result = await playlistClient.GetSimilarRawAsync(ids);
        return Ok(result);
    }

    /// <summary>
    ///     音效歌单。
    /// </summary>
    /// <param name="page">页数。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <returns>音效歌单列表。</returns>
    [HttpGet("effect")]
    public async Task<IActionResult> GetEffect([FromQuery] int page = 1, [FromQuery] int pagesize = 30)
    {
        var result = await playlistClient.GetSoundEffectRawAsync(page, pagesize);
        return Ok(result);
    }

    /// <summary>
    ///     收藏歌单。
    /// </summary>
    /// <param name="name">歌单名称。</param>
    /// <param name="sourceGlobalId">歌单 ID。</param>
    /// <returns>收藏歌单结果。</returns>
    [HttpPost("add")]
    public async Task<IActionResult> AddPlaylist(
        [FromQuery][Required(AllowEmptyStrings = false)] string name,
        [FromQuery(Name = "list_create_gid")][Required(AllowEmptyStrings = false)] string sourceGlobalId)
    {
        var result = await playlistClient.CollectPlaylistAsync(name, sourceGlobalId);
        return Ok(result);
    }

    /// <summary>
    ///     新建歌单。
    /// </summary>
    /// <param name="name">歌单名称。</param>
    /// <param name="type">是否设为隐私，0：公开，1：隐私，仅支持创建歌单时传入。</param>
    /// <returns>新建歌单结果。</returns>
    [HttpPost("create")]
    public async Task<IActionResult> CreatePlaylist(
        [FromQuery][Required(AllowEmptyStrings = false)] string name,
        [FromQuery(Name = "type")] long type = 0)
    {
        var result = await playlistClient.CreatePlaylistAsync(name, type);
        return Ok(result);
    }

    /// <summary>
    ///     取消收藏 / 删除歌单。
    /// </summary>
    /// <param name="listid">用户歌单 listid。</param>
    /// <returns>删除歌单结果。</returns>
    [HttpPost("del")]
    public async Task<IActionResult> DeletePlaylist([FromQuery][Required(AllowEmptyStrings = false)] string listid)
    {
        var result = await playlistClient.DeletePlaylistAsync(listid);
        return Ok(result);
    }

    /// <summary>
    ///     对歌单添加歌曲
    /// </summary>
    /// <param name="request">要添加的歌曲列表。</param>
    /// <returns>添加歌曲结果。</returns>
    [HttpPost("tracks/add")]
    [ProducesResponseType(typeof(AddSongResponse), StatusCodes.Status200OK)]
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
    /// </summary>
    /// <param name="listid">目标歌单 ID。</param>
    /// <param name="fileids">歌曲 fileid，多个值用英文逗号分隔。</param>
    /// <returns>删除歌曲结果。</returns>
    [HttpPost("tracks/del")]
    [ProducesResponseType(typeof(RemoveSongResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteTracks(
        [FromQuery][Required(AllowEmptyStrings = false)] string listid,
        [FromQuery][Required(AllowEmptyStrings = false)] string fileids)
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
