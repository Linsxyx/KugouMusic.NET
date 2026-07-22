using KgWebApi.Net.Extensions;
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
    /// <summary>
    ///     获取音乐相关信息。
    /// </summary>
    /// <param name="hash">歌曲 hash，可以传多个，每个以逗号分开。</param>
    /// <returns>音乐相关信息。</returns>
    [HttpGet("/audio")]
    public async Task<IActionResult> GetAudio([FromQuery][Required(AllowEmptyStrings = false)] string hash)
    {
        return Ok(await songClient.GetAudioAsync(hash));
    }

    /// <summary>
    ///     获取更多音乐版本。
    /// </summary>
    /// <param name="albumAudioId">音乐的 mixsongid/album_audio_id。</param>
    /// <param name="page">页码。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <param name="sort">排序，支持 all、hot、new。</param>
    /// <param name="type">分类。</param>
    /// <param name="showType">是否返回分类。</param>
    /// <param name="showDetail">是否返回详情，否则只返回总数。</param>
    /// <returns>更多版本音乐信息。</returns>
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

    /// <summary>
    ///     获取音乐伴奏信息。
    /// </summary>
    /// <param name="hash">音乐 hash。</param>
    /// <param name="fileName">音乐 fileName。</param>
    /// <param name="mixId">音乐的 mixsongid/album_audio_id。</param>
    /// <returns>最佳伴奏信息。</returns>
    [HttpGet("/audio/accompany/matching")]
    public async Task<IActionResult> GetAudioAccompanyMatching(
        [FromQuery][Required(AllowEmptyStrings = false)] string hash,
        [FromQuery][Required(AllowEmptyStrings = false)] string fileName,
        [FromQuery][Required] long? mixId)
    {
        return Ok(await songClient.GetAudioAccompanyMatchingAsync(hash, mixId!.Value, fileName));
    }

    /// <summary>
    ///     听歌识曲。POST 原始 PCM 音频数据（16-bit，application/octet-stream）。
    /// </summary>
    /// <returns>识曲结果。</returns>
    [HttpPost("/audio/match")]
    [Consumes("application/octet-stream")]
    [ProducesResponseType(typeof(AudioMatchResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> MatchAudio()
    {
        var pcmData = await ReadBodyAsync(
            Request.Body,
            Request.ContentLength,
            HttpContext.RequestAborted);
        if (pcmData.Length == 0)
        {
            return BadRequest(new
            {
                status = 0,
                message = "请求体不能为空，请以 application/octet-stream 提交 PCM 音频数据。"
            });
        }

        return Ok(await songClient.GetAudioMatchAsync(pcmData));
    }

    /// <summary>
    ///     听歌识曲。使用 multipart/form-data 上传原始 PCM 音频文件。
    /// </summary>
    /// <param name="request">表单上传请求，字段名为 file。</param>
    /// <returns>识曲结果。</returns>
    [HttpPost("/audio/match")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(AudioMatchResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> MatchAudioForm([FromForm] AudioMatchUploadRequest request)
    {
        if (request.File is null || request.File.Length == 0)
        {
            return BadRequest(new
            {
                status = 0,
                message = "表单文件不能为空，请使用 multipart/form-data 并提供 file 字段。"
            });
        }

        await using var stream = request.File.OpenReadStream();
        var pcmData = await ReadBodyAsync(stream, request.File.Length, HttpContext.RequestAborted);
        return Ok(await songClient.GetAudioMatchAsync(pcmData));
    }

    private static async Task<byte[]> ReadBodyAsync(
        Stream source,
        long? length,
        CancellationToken cancellationToken)
    {
        if (length is >= 0 and <= int.MaxValue)
        {
            var bytes = GC.AllocateUninitializedArray<byte>((int)length.Value);
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = await source.ReadAsync(bytes.AsMemory(offset), cancellationToken);
                if (read == 0)
                {
                    Array.Resize(ref bytes, offset);
                    break;
                }

                offset += read;
            }

            return bytes;
        }

        await using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    /// <summary>
    ///     获取音乐 K 歌数量。
    /// </summary>
    /// <param name="songId">音乐 songid。</param>
    /// <param name="songHash">音乐 hash。</param>
    /// <param name="singerName">歌手名称，多个以 `、` 隔开。</param>
    /// <returns>音乐 K 歌数量。</returns>
    [HttpGet("/audio/ktv/total")]
    public async Task<IActionResult> GetAudioKtvTotal(
        [FromQuery][BindRequired] long songId,
        [FromQuery][Required(AllowEmptyStrings = false)] string songHash,
        [FromQuery][Required(AllowEmptyStrings = false)] string singerName)
    {
        return Ok(await songClient.GetAudioKtvTotalAsync(songId, songHash, singerName));
    }

    /// <summary>
    ///     获取歌曲高潮部分。
    /// </summary>
    /// <param name="hash">音乐 hash，可以传多个，以逗号分割。</param>
    /// <returns>歌曲高潮时间信息。</returns>
    [HttpGet("climax")]
    public async Task<IActionResult> GetClimax([FromQuery][Required(AllowEmptyStrings = false)] string hash)
    {
        return Ok(await songClient.GetSongClimaxAsync(hash));
    }

    /// <summary>
    ///     歌曲详情 - 歌曲成绩单。
    /// </summary>
    /// <param name="albumAudioId">专辑音乐 id (album_audio_id/MixSongID 均可以)。</param>
    /// <returns>歌曲成绩单信息。</returns>
    [HttpGet("ranking")]
    public async Task<IActionResult> GetRanking([FromQuery(Name = "album_audio_id")][Required(AllowEmptyStrings = false)] string albumAudioId)
    {
        return Ok(await songClient.GetSongRankingAsync(albumAudioId));
    }

    /// <summary>
    ///     歌曲详情 - 歌曲成绩单详情。
    /// </summary>
    /// <param name="albumAudioId">专辑音乐 id (album_audio_id/MixSongID 均可以)。</param>
    /// <param name="page">页数。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <returns>更详细的歌曲成绩单信息。</returns>
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

    /// <summary>
    ///     获取音乐专辑/歌手信息。
    /// </summary>
    /// <param name="albumAudioIds">专辑音乐 id (album_audio_id/MixSongID 均可以)，可以传多个。</param>
    /// <param name="fields">可选字段集合。</param>
    /// <returns>音乐专辑/歌手信息。</returns>
    [HttpGet("/kmr/audio")]
    public async Task<IActionResult> GetKmrAudio(
        [FromQuery(Name = "album_audio_id")][Required(AllowEmptyStrings = false)] string albumAudioIds,
        [FromQuery] string? fields = "base")
    {
        return Ok(await songClient.GetKmrAudioAsync(albumAudioIds, fields));
    }

    /// <summary>
    ///     获取音乐详情。
    /// </summary>
    /// <param name="hash">歌曲 hash，可以传多个，每个以逗号分开。</param>
    /// <param name="albumIds">专辑 id。</param>
    /// <returns>音乐详情。</returns>
    [HttpGet("/privilege/lite")]
    [ProducesResponseType(typeof(List<PrivilegeLiteData>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPrivilegeLite(
        [FromQuery][Required(AllowEmptyStrings = false)] string hash,
        [FromQuery(Name = "album_id")] string? albumIds = null)
    {
        return Ok(await songClient.GetPrivilegeLiteAsync(hash, albumIds));
    }

    /// <summary>
    ///     获取歌手和专辑图片。
    /// </summary>
    /// <param name="hash">歌曲 hash，可以传多个，每个以逗号分开。</param>
    /// <param name="albumIds">专辑 id，可以传多个。</param>
    /// <param name="albumAudioIds">专辑音乐 id，可以传多个。</param>
    /// <param name="count">最多返回多少张图片。</param>
    /// <returns>歌手和专辑图片信息。</returns>
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

    /*/// <summary>
    ///     获取音乐 URL（新版）。
    /// </summary>
    /// <param name="hash">音乐 hash。</param>
    /// <param name="albumAudioId">专辑音频 id。</param>
    /// <param name="freePart">是否返回试听部分。</param>
    /// <returns>新版音乐 URL 信息。</returns>
    [HttpGet("url/new")]
    public async Task<IActionResult> GetUrlNew(
        [FromQuery][Required(AllowEmptyStrings = false)] string hash,
        [FromQuery(Name = "album_audio_id")] string? albumAudioId = null,
        [FromQuery(Name = "free_part")] bool freePart = false)
    {
        return Ok(await songClient.GetUrlNewAsync(hash, albumAudioId, freePart));
    }*/

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
        var result = await songClient.GetPlayInfoAsync(hash, quality, albumId, albumAudioId, freePart);
        return this.FromKgStatus(result);
    }
}

public sealed class AudioMatchUploadRequest
{
    [FromForm(Name = "file")]
    [Required]
    public IFormFile? File { get; init; }
}
