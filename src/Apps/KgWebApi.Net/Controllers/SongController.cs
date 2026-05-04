using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("song")]
public class SongController(SongClient songClient) : ControllerBase
{
    [HttpGet("/audio")]
    public async Task<IActionResult> GetAudio([FromQuery] string hash)
    {
        return Ok(await songClient.GetAudioAsync(hash));
    }

    [HttpGet("/audio/related")]
    public async Task<IActionResult> GetAudioRelated(
        [FromQuery(Name = "album_audio_id")] long albumAudioId,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30,
        [FromQuery] string sort = "all",
        [FromQuery] int type = 0,
        [FromQuery(Name = "show_type")] int showType = 0,
        [FromQuery(Name = "show_detail")] int showDetail = 1)
    {
        return Ok(await songClient.GetAudioRelatedAsync(
            albumAudioId,
            page,
            pagesize,
            sort,
            type,
            showType,
            showDetail != 0));
    }

    [HttpGet("/audio/accompany/matching")]
    public async Task<IActionResult> GetAudioAccompanyMatching(
        [FromQuery] string hash,
        [FromQuery] long mixId = 0,
        [FromQuery] string? fileName = null)
    {
        return Ok(await songClient.GetAudioAccompanyMatchingAsync(hash, mixId, fileName));
    }

    [HttpGet("/audio/ktv/total")]
    public async Task<IActionResult> GetAudioKtvTotal(
        [FromQuery] long songId,
        [FromQuery] string songHash,
        [FromQuery] string singerName)
    {
        return Ok(await songClient.GetAudioKtvTotalAsync(songId, songHash, singerName));
    }

    [HttpGet("climax")]
    public async Task<IActionResult> GetClimax([FromQuery] string hash)
    {
        return Ok(await songClient.GetSongClimaxAsync(hash));
    }

    [HttpGet("ranking")]
    public async Task<IActionResult> GetRanking([FromQuery(Name = "album_audio_id")] string albumAudioId)
    {
        return Ok(await songClient.GetSongRankingAsync(albumAudioId));
    }

    [HttpGet("ranking/filter")]
    public async Task<IActionResult> GetRankingFilter(
        [FromQuery(Name = "album_audio_id")] string albumAudioId,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await songClient.GetSongRankingFilterAsync(albumAudioId, page, pagesize));
    }

    [HttpGet("/kmr/audio/mv")]
    public async Task<IActionResult> GetKmrAudioMv(
        [FromQuery(Name = "album_audio_id")] string albumAudioIds,
        [FromQuery] string? fields = null)
    {
        return Ok(await songClient.GetKmrAudioMvAsync(albumAudioIds, fields));
    }

    [HttpGet("/kmr/audio")]
    public async Task<IActionResult> GetKmrAudio(
        [FromQuery(Name = "album_audio_id")] string albumAudioIds,
        [FromQuery] string? fields = "base")
    {
        return Ok(await songClient.GetKmrAudioAsync(albumAudioIds, fields));
    }

    [HttpGet("/privilege/lite")]
    public async Task<IActionResult> GetPrivilegeLite(
        [FromQuery] string hash,
        [FromQuery(Name = "album_id")] string? albumIds = null)
    {
        return Ok(await songClient.GetPrivilegeLiteAsync(hash, albumIds));
    }

    [HttpGet("/images")]
    public async Task<IActionResult> GetImages(
        [FromQuery] string hash,
        [FromQuery(Name = "album_id")] string? albumIds = null,
        [FromQuery(Name = "album_audio_id")] string? albumAudioIds = null,
        [FromQuery] int count = 5)
    {
        return Ok(await songClient.GetImagesAsync(hash, albumIds, albumAudioIds, count));
    }

    [HttpGet("/images/audio")]
    public async Task<IActionResult> GetAudioImages(
        [FromQuery] string hash,
        [FromQuery(Name = "audio_id")] string? audioIds = null,
        [FromQuery(Name = "album_audio_id")] string? albumAudioIds = null,
        [FromQuery(Name = "filename")] string? fileNames = null,
        [FromQuery] int count = 5)
    {
        return Ok(await songClient.GetAudioImagesAsync(hash, audioIds, albumAudioIds, fileNames, count));
    }

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
