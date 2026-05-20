using KgWebApi.Net.Extensions;
using KuGou.Net.ExternalPlaylists;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("playlist/external")]
public class ExternalPlaylistController(IExternalPlaylistParser externalPlaylistParser) : ControllerBase
{
    /// <summary>
    ///     解析网易云 / QQ 音乐歌单分享链接，返回歌单名和歌曲名称列表。
    /// </summary>
    /// <param name="request">分享链接或分享文案。</param>
    /// <returns>解析结果。</returns>
    [HttpPost("parse")]
    [ProducesResponseType(typeof(ExternalPlaylistParseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Parse([FromBody] ExternalPlaylistParseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourceText))
            return this.ApiBadRequest("sourceText 不能为空", 40007);

        var result = await externalPlaylistParser.ParseAndLoadAsync(request.SourceText);
        if (!result.Success)
            return this.ApiBadRequest(result.ErrorMessage, 40008);

        return Ok(new ExternalPlaylistParseResponse
        {
            SourcePlatform = result.SourcePlatform,
            PlaylistName = result.SourcePlaylistName,
            SongNames = result.SongNames
        });
    }
}

public sealed class ExternalPlaylistParseRequest
{
    public string SourceText { get; init; } = string.Empty;
}

public sealed class ExternalPlaylistParseResponse
{
    public string SourcePlatform { get; init; } = string.Empty;
    public string PlaylistName { get; init; } = string.Empty;
    public List<string> SongNames { get; init; } = new();
}
