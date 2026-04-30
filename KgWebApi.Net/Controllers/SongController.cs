using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("song")]
public class SongController(SongClient songClient) : ControllerBase
{
    [HttpGet("url/new")]
    public async Task<IActionResult> GetUrlNew(
        [FromQuery] string hash,
        [FromQuery(Name = "album_audio_id")] string? albumAudioId = null,
        [FromQuery(Name = "free_part")] bool freePart = false)
    {
        return Ok(await songClient.GetUrlNewAsync(hash, albumAudioId, freePart));
    }

    [HttpGet("url")]
    public async Task<IActionResult> GetUrl(
        [FromQuery] string hash,
        [FromQuery] string quality = "128",
        [FromQuery(Name = "album_id")] string? albumId = null,
        [FromQuery(Name = "album_audio_id")] string? albumAudioId = null,
        [FromQuery(Name = "free_part")] bool freePart = false)
    {
        return Ok(await songClient.GetPlayInfoAsync(hash, quality));
    }
}
