using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace KgWebApi.Net.Controllers;

/// <summary>
///     登录相关API接口
/// </summary>
[ApiController]
[Route("login")]
public class LoginController(LoginClient loginClient, ILogger<LoginController> logger) : ControllerBase
{
    /// <summary>
    ///     登录。
    /// </summary>
    /// <param name="req">手机号和短信验证码。</param>
    /// <returns>登录结果和账号 Token 信息。轮询此接口可获取二维码扫码状态, 408 为等待扫描，404 为已经扫描，403 为拒绝登录，405 为登录成功，402 为已过期</returns>
    [HttpPost("cellphone")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> LoginByMobile([FromBody] MobileLoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Mobile) || string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { status = 0, msg = "手机号和验证码不能为空" });

        try
        {
            var result = await loginClient.LoginByMobileAsync(req.Mobile, req.Code);
            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "手机登录异常");
            return StatusCode(500, new { status = 0, msg = ex.Message });
        }
    }

    /// <summary>
    ///     二维码登录 - 二维码 key 生成接口。
    /// </summary>
    /// <returns>二维码 Key、登录链接和展示信息。</returns>
    [HttpGet("qr/key")]
    [ProducesResponseType(typeof(QRCode), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQrKey()
    {
        var result = await loginClient.GetQrCodeAsync();

        return Ok(result);
    }

    /// <summary>
    ///     二维码登录 - 二维码检测扫码状态接口。
    /// </summary>
    /// <param name="key">二维码 Key</param>
    /// <returns>二维码登录状态。</returns>
    [HttpGet("qr/check")]
    [ProducesResponseType(typeof(QrLoginStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckQrCode([FromQuery][Required(AllowEmptyStrings = false)] string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return BadRequest(new { status = 0, msg = "Key 不能为空" });

        var result = await loginClient.CheckQrStatusAsync(key);

        return Ok(result);
    }

    /// <summary>
    ///     刷新登录。
    /// </summary>
    /// <returns>刷新后的 Token 信息。</returns>
    [HttpPost("token")]
    [ProducesResponseType(typeof(RefreshTokenResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RefreshToken()
    {
        var result = await loginClient.RefreshSessionAsync();

        return Ok(result);
    }

    /// <summary>
    ///     退出登录。
    /// </summary>
    /// <returns>退出登录结果。</returns>
    [HttpPost("logout")]
    public Task<IActionResult> LogOut()
    {
        loginClient.LogOutAsync();
        return Task.FromResult<IActionResult>(Ok());
    }
}

// ================= DTO 模型 =================

public record MobileLoginRequest(
    [param: Required(AllowEmptyStrings = false)] string Mobile,
    [param: Required(AllowEmptyStrings = false)] string Code);
