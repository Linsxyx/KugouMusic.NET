using System.Text.Json;
using KgWebApi.Net.Extensions;
using KgWebApi.Net.Models;
using KgWebApi.Net.Services;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("audio/match")]
public sealed class AudioMatchController(
    NeteaseAudioMatchService audioMatchService,
    ILogger<AudioMatchController> logger) : ControllerBase
{
    /// <summary>
    ///     网易云听歌识曲。传入 ncm-afp 生成的音频指纹。
    /// </summary>
    /// <param name="duration">音频时长，单位秒。兼容 demo 的 query 写法。</param>
    /// <param name="audioFP">ncm-afp 生成的 base64 音频指纹。兼容 demo 的 query 写法。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>网易云听歌识曲结果。</returns>
    [HttpPost]
    [ProducesResponseType(typeof(NeteaseAudioMatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> MatchByPost(
        [FromQuery] int? duration,
        [FromQuery] string? audioFP,
        CancellationToken cancellationToken)
    {
        var request = await ReadJsonRequestIfPresent(cancellationToken);
        return await MatchFingerprintCore(
            request?.Duration ?? duration.GetValueOrDefault(),
            request?.AudioFP ?? audioFP ?? string.Empty,
            cancellationToken);
    }

    /// <summary>
    ///     网易云听歌识曲。请求体传入 8 kHz、单声道、little-endian Float32 PCM。
    /// </summary>
    /// <param name="duration">音频时长，单位秒。不传时会按 8 kHz 样本数估算。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>网易云听歌识曲结果，包含生成的 audioFP。</returns>
    [HttpPost("pcm")]
    [Consumes("application/octet-stream")]
    [ProducesResponseType(typeof(NeteaseAudioMatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> MatchPcm(
        [FromQuery] int? duration,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await audioMatchService.MatchPcmAsync(Request.Body, duration, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return this.ApiBadRequest(ex.Message, 40031);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "网易云听歌识曲请求超时");
            return this.ApiGatewayTimeout("网易云听歌识曲请求超时", 50431);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "网易云听歌识曲请求失败");
            return this.ApiGatewayTimeout("网易云听歌识曲上游请求失败", 50432);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "网易云听歌识曲异常");
            return this.ApiServerError($"内部服务器错误: {ex.Message}", 50031);
        }
    }

    private async Task<IActionResult> MatchFingerprintCore(
        int duration,
        string audioFP,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await audioMatchService.MatchFingerprintAsync(duration, audioFP, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return this.ApiBadRequest(ex.Message, 40030);
        }
        catch (ArgumentException ex)
        {
            return this.ApiBadRequest(ex.Message, 40030);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "网易云听歌识曲请求超时");
            return this.ApiGatewayTimeout("网易云听歌识曲请求超时", 50430);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "网易云听歌识曲请求失败");
            return this.ApiGatewayTimeout("网易云听歌识曲上游请求失败", 50430);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "网易云听歌识曲异常");
            return this.ApiServerError($"内部服务器错误: {ex.Message}", 50030);
        }
    }

    private async Task<NeteaseAudioMatchRequest?> ReadJsonRequestIfPresent(CancellationToken cancellationToken)
    {
        if (Request.ContentLength is 0 || string.IsNullOrWhiteSpace(Request.ContentType))
        {
            return null;
        }

        if (!Request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return await JsonSerializer.DeserializeAsync<NeteaseAudioMatchRequest>(
            Request.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);
    }
}
