using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
public class LyricController(LyricClient lyricClient) : ControllerBase
{
    [HttpGet("search/lyric")]
    public async Task<IActionResult> SearchLyric(
        [FromQuery] string? hash,
        [FromQuery(Name = "album_audio_id")] string? albumAudioId,
        [FromQuery] string? keywords,
        [FromQuery] string? man)
    {
        var result = await lyricClient.SearchLyricAsync(hash, albumAudioId, keywords, man);
        return Ok(result);
    }

    [HttpGet("lyric")]
    public async Task<IActionResult> GetLyric(
        [FromQuery] string id,
        [FromQuery] string accesskey,
        [FromQuery] string fmt = "krc",
        [FromQuery] bool decode = true)
    {
        var result = await lyricClient.GetLyricAsync(id, accesskey, fmt, decode);
        return Ok(result);
    }
}
