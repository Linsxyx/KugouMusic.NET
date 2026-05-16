using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.ComponentModel.DataAnnotations;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("song")]
public class SongController(SongClient songClient) : ControllerBase
{
    [HttpGet("/audio")]
    public async Task<IActionResult> GetAudio([FromQuery][Required(AllowEmptyStrings = false)] string hash)
    {
        return Ok(await songClient.GetAudioAsync(hash));
    }

    [HttpGet("/audio/related")]
    public async Task<IActionResult> GetAudioRelated(
        [FromQuery(Name = "album_audio_id")][BindRequired] long albumAudioId,
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
        [FromQuery][Required(AllowEmptyStrings = false)] string hash,
        [FromQuery][Required(AllowEmptyStrings = false)] string fileName,
        [FromQuery][Required] long? mixId)
    {
        return Ok(await songClient.GetAudioAccompanyMatchingAsync(hash, mixId!.Value, fileName));
    }

    [HttpGet("/audio/ktv/total")]
    public async Task<IActionResult> GetAudioKtvTotal(
        [FromQuery][BindRequired] long songId,
        [FromQuery][Required(AllowEmptyStrings = false)] string songHash,
        [FromQuery][Required(AllowEmptyStrings = false)] string singerName)
    {
        return Ok(await songClient.GetAudioKtvTotalAsync(songId, songHash, singerName));
    }

    [HttpGet("climax")]
    public async Task<IActionResult> GetClimax([FromQuery][Required(AllowEmptyStrings = false)] string hash)
    {
        return Ok(await songClient.GetSongClimaxAsync(hash));
    }

    [HttpGet("ranking")]
    public async Task<IActionResult> GetRanking([FromQuery(Name = "album_audio_id")][Required(AllowEmptyStrings = false)] string albumAudioId)
    {
        return Ok(await songClient.GetSongRankingAsync(albumAudioId));
    }

    [HttpGet("ranking/filter")]
    public async Task<IActionResult> GetRankingFilter(
        [FromQuery(Name = "album_audio_id")][Required(AllowEmptyStrings = false)] string albumAudioId,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        return Ok(await songClient.GetSongRankingFilterAsync(albumAudioId, page, pagesize));
    }

    /// <summary>
    ///     获取歌曲 MV。
    /// </summary>
    /// <param name="albumAudioIds">专辑音乐 id (album_audio_id/MixSongID 均可以)</param>
    /// <param name="fields">支持的值有：mkv,tags,h264,h265,authors</param>
    /// <returns>歌曲 相对应的 mv。</returns>
    [HttpGet("/kmr/audio/mv")]
    [ProducesResponseType(typeof(AudioMvResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetKmrAudioMv(
        [FromQuery(Name = "album_audio_id")][Required(AllowEmptyStrings = false)] string albumAudioIds,
        [FromQuery] string? fields = null)
    {
        return Ok(await songClient.GetKmrAudioMvAsync(albumAudioIds, fields));
    }

    [HttpGet("/kmr/audio")]
    public async Task<IActionResult> GetKmrAudio(
        [FromQuery(Name = "album_audio_id")][Required(AllowEmptyStrings = false)] string albumAudioIds,
        [FromQuery] string? fields = "base")
    {
        return Ok(await songClient.GetKmrAudioAsync(albumAudioIds, fields));
    }

    [HttpGet("/privilege/lite")]
    public async Task<IActionResult> GetPrivilegeLite(
        [FromQuery][Required(AllowEmptyStrings = false)] string hash,
        [FromQuery(Name = "album_id")] string? albumIds = null)
    {
        return Ok(await songClient.GetPrivilegeLiteAsync(hash, albumIds));
    }

    [HttpGet("/images")]
    public async Task<IActionResult> GetImages(
        [FromQuery][Required(AllowEmptyStrings = false)] string hash,
        [FromQuery(Name = "album_id")] string? albumIds = null,
        [FromQuery(Name = "album_audio_id")] string? albumAudioIds = null,
        [FromQuery] int count = 5)
    {
        return Ok(await songClient.GetImagesAsync(hash, albumIds, albumAudioIds, count));
    }

    /// <summary>
    ///     获取歌曲图片。
    /// </summary>
    /// <param name="hash">歌曲 Hash。</param>
    /// <param name="audioIds">音频 ID，多个值可用英文逗号分隔。</param>
    /// <param name="albumAudioIds">专辑音频 ID，多个值可用英文逗号分隔。</param>
    /// <param name="fileNames">文件名，多个值可用英文逗号分隔。</param>
    /// <param name="count">返回数量。</param>
    /// <returns>歌曲图片结果。</returns>
    [HttpGet("/images/audio")]
    [ProducesResponseType(typeof(AudioImageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAudioImages(
        [FromQuery][Required(AllowEmptyStrings = false)] string hash,
        [FromQuery(Name = "audio_id")] string? audioIds = null,
        [FromQuery(Name = "album_audio_id")] string? albumAudioIds = null,
        [FromQuery(Name = "filename")] string? fileNames = null,
        [FromQuery] int count = 5)
    {
        return Ok(await songClient.GetAudioImagesAsync(hash, audioIds, albumAudioIds, fileNames, count));
    }

    [HttpGet("url/new")]
    public async Task<IActionResult> GetUrlNew(
        [FromQuery][Required(AllowEmptyStrings = false)] string hash,
        [FromQuery(Name = "album_audio_id")] string? albumAudioId = null,
        [FromQuery(Name = "free_part")] bool freePart = false)
    {
        return Ok(await songClient.GetUrlNewAsync(hash, albumAudioId, freePart));
    }

    /// <summary>
    ///     获取歌曲播放地址。
    /// </summary>
    /// <param name="hash">歌曲 Hash。</param>
    /// <param name="quality">音质。</param>
    /// <param name="albumId">专辑 ID。</param>
    /// <param name="albumAudioId">专辑音频 ID。</param>
    /// <param name="freePart">是否只获取试听片段。</param>
    /// <returns>播放地址和音频信息。</returns>
    [HttpGet("url")]
    [ProducesResponseType(typeof(PlayUrlData), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUrl(
        [FromQuery][Required(AllowEmptyStrings = false)] string hash,
        [FromQuery] string quality = "128",
        [FromQuery(Name = "album_id")] string? albumId = null,
        [FromQuery(Name = "album_audio_id")] string? albumAudioId = null,
        [FromQuery(Name = "free_part")] bool freePart = false)
    {
        return Ok(await songClient.GetPlayInfoAsync(hash, quality));
    }
}
