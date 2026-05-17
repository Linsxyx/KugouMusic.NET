using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace KgWebApi.Net.Controllers;

/// <summary>
///     搜索相关API接口
/// </summary>
[ApiController]
[Route("search")]
public class SearchController(
    SearchClient searchClient,
    ILogger<SearchController> logger)
    : ControllerBase
{
    /// <summary>
    ///     搜索。
    /// </summary>
    /// <param name="keywords">关键词。</param>
    /// <param name="page">页数。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <param name="type">搜索类型。默认为单曲，special：歌单，lyric：歌词，song：单曲，album：专辑</param>
    /// <returns>搜索结果。</returns>
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery][Required(AllowEmptyStrings = false)] string keywords = "",
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30,
        [FromQuery] string type = "song")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(keywords)) return BadRequest(new { error = "关键词不能为空" });

            logger.LogInformation("开始搜索，关键词: {Keywords}, 页码: {Page}", keywords, page);

            object? result = type switch
            {
                "special" => await searchClient.SearchSpecialAsync(keywords, page),
                "album" => await searchClient.SearchAlbumAsync(keywords, page),
                "song" => await searchClient.SearchAsync(keywords, page, type),
                _ => await searchClient.SearchRawAsync(keywords, page, pagesize, type)
            };

            return Ok(result);
        }
        catch (TaskCanceledException)
        {
            logger.LogWarning("搜索请求超时或取消");
            return StatusCode(504, new { error = "请求超时" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "搜索异常，关键词: {Keywords}", keywords);
            return StatusCode(500, new { error = $"内部服务器错误: {ex.Message}" });
        }
    }

    /// <summary>
    ///     搜索歌单。
    /// </summary>
    /// <param name="keywords">搜索关键词。</param>
    /// <param name="page">页码。</param>
    /// <returns>匹配的歌单列表。</returns>
    [HttpGet("special")]
    [ProducesResponseType(typeof(List<SearchPlaylistItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchSpecial(
        [FromQuery][Required(AllowEmptyStrings = false)] string keywords = "",
        [FromQuery] int page = 1)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(keywords)) return BadRequest(new { error = "关键词不能为空" });

            logger.LogInformation("开始搜索，关键词: {Keywords}, 页码: {Page}", keywords, page);

            var result = await searchClient.SearchSpecialAsync(keywords, page);

            return Ok(result);
        }
        catch (TaskCanceledException)
        {
            logger.LogWarning("搜索请求超时或取消");
            return StatusCode(504, new { error = "请求超时" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "搜索异常，关键词: {Keywords}", keywords);
            return StatusCode(500, new { error = $"内部服务器错误: {ex.Message}" });
        }
    }

    /// <summary>
    ///     搜索专辑。
    /// </summary>
    /// <param name="keywords">搜索关键词。</param>
    /// <param name="page">页码。</param>
    /// <returns>匹配的专辑列表。</returns>
    [HttpGet("album")]
    [ProducesResponseType(typeof(List<SearchAlbumItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAlbum(
        [FromQuery][Required(AllowEmptyStrings = false)] string keywords = "",
        [FromQuery] int page = 1)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(keywords)) return BadRequest(new { error = "关键词不能为空" });

            logger.LogInformation("开始搜索，关键词: {Keywords}, 页码: {Page}", keywords, page);

            var result = await searchClient.SearchAlbumAsync(keywords, page);

            return Ok(result);
        }
        catch (TaskCanceledException)
        {
            logger.LogWarning("搜索请求超时或取消");
            return StatusCode(504, new { error = "请求超时" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "搜索异常，关键词: {Keywords}", keywords);
            return StatusCode(500, new { error = $"内部服务器错误: {ex.Message}" });
        }
    }

    /// <summary>
    ///     热搜列表。
    /// </summary>
    /// <returns>热搜关键词和热度信息。</returns>
    [HttpGet("hot")]
    [ProducesResponseType(typeof(SearchHotResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHot()
    {
        var result = await searchClient.GetSearchHotAsync();
        return Ok(result);
    }

    /// <summary>
    ///     默认搜索关键词。
    /// </summary>
    /// <returns>默认搜索关键词。</returns>
    [HttpGet("default")]
    public async Task<IActionResult> GetDefault()
    {
        var result = await searchClient.SearchDefaultRawAsync();
        return Ok(result);
    }

    /// <summary>
    ///     搜索建议。
    /// </summary>
    /// <param name="keywords">搜索关键词。</param>
    /// <param name="albumTipCount">专辑返回数量。</param>
    /// <param name="correctTipCount">纠正提示数量。</param>
    /// <param name="mvTipCount">MV 返回数量。</param>
    /// <param name="musicTipCount">音乐返回数量。</param>
    /// <returns>搜索建议结果。</returns>
    [HttpGet("suggest")]
    public async Task<IActionResult> GetSuggest(
        [FromQuery][Required(AllowEmptyStrings = false)] string keywords,
        [FromQuery] int albumTipCount = 10,
        [FromQuery] int correctTipCount = 10,
        [FromQuery] int mvTipCount = 10,
        [FromQuery] int musicTipCount = 10)
    {
        var result = await searchClient.SearchSuggestRawAsync(
            keywords,
            albumTipCount,
            correctTipCount,
            mvTipCount,
            musicTipCount);
        return Ok(result);
    }

    /// <summary>
    ///     综合搜索。
    /// </summary>
    /// <param name="keyword">搜索关键词。</param>
    /// <returns>综合搜索结果。</returns>
    [HttpGet("mixed")]
    public async Task<IActionResult> GetMixed([FromQuery][Required(AllowEmptyStrings = false)] string keyword)
    {
        var result = await searchClient.SearchMixedRawAsync(keyword);
        return Ok(result);
    }

    /// <summary>
    ///     综合搜索。
    /// </summary>
    /// <param name="keywords">关键词。</param>
    /// <param name="page">页数。</param>
    /// <param name="pagesize">每页页数。</param>
    /// <returns>综合搜索结果。</returns>
    [HttpGet("complex")]
    public async Task<IActionResult> GetComplex(
        [FromQuery][Required(AllowEmptyStrings = false)] string keywords,
        [FromQuery] int page = 1,
        [FromQuery] int pagesize = 30)
    {
        var result = await searchClient.SearchComplexRawAsync(keywords, page, pagesize);
        return Ok(result);
    }
}
