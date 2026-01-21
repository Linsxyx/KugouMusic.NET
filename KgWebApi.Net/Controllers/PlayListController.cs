using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("[controller]")] // 路由前缀: /PlayList
public class PlayListController(PlaylistClient playlistClient) : ControllerBase
{
    [HttpGet("detail")]
    public async Task<IActionResult> GetDetail([FromQuery] string globalcollectionid)
    {
        var result = await playlistClient.GetInfoAsync(globalcollectionid);
        return Ok(result);
    }

    [HttpGet("Tags")]
    public async Task<IActionResult> GetDetail()
    {
        var result = await playlistClient.GetTagsAsync();
        return Ok(result);
    }

    [HttpGet("track/all")]
    public async Task<IActionResult> GetDtrackAll(
        [FromQuery] string globalcollectionid,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        var result = await playlistClient.GetSongsAsync(globalcollectionid, page, pagesize);
        return Ok(result);
    }

    /// <summary>
    ///     收藏歌单 / 新建歌单
    ///     路由: POST /PlayList/add
    /// </summary>
    [HttpPost("add")]
    public async Task<IActionResult> AddPlaylist(
        [FromQuery] string name,
        [FromQuery(Name = "list_create_userid")]
        string sourceUserId,
        [FromQuery(Name = "list_create_listid")]
        string sourceListId,
        [FromQuery(Name = "list_create_gid")] string? sourceGlobalId,
        [FromQuery(Name = "type")] long type)
    {
        var result = await playlistClient.CollectPlaylistAsync(name, sourceUserId, sourceListId, sourceGlobalId, type);
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