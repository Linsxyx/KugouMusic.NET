using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

/// <summary>
///     登录相关API接口 - 使用新的KuGou.Net库
/// </summary>
[ApiController]
[Route("[controller]")]
public class LoginController(AuthClient authClient, ILogger<LoginController> logger) : ControllerBase
{
    /// <summary>
    ///     手机验证码登录
    /// </summary>
    [HttpPost("mobile")]
    public async Task<IActionResult> LoginByMobile([FromBody] MobileLoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Mobile) || string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { status = 0, msg = "手机号和验证码不能为空" });

        try
        {
            var result = await authClient.LoginByMobileAsync(req.Mobile, req.Code);
            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "手机登录异常");
            return StatusCode(500, new { status = 0, msg = ex.Message });
        }
    }

    /// <summary>
    ///     获取二维码 Key 和链接
    /// </summary>
    [HttpGet("qrcode/key")]
    public async Task<IActionResult> GetQrKey()
    {
        var result = await authClient.GetQrCodeAsync();

        return Ok(result);
    }

    /// <summary>
    ///     检查二维码扫码状态
    /// </summary>
    /// <param name="key">二维码 Key</param>
    [HttpGet("qrcode/check")]
    public async Task<IActionResult> CheckQrCode([FromQuery] string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return BadRequest(new { status = 0, msg = "Key 不能为空" });

        var result = await authClient.CheckQrStatusAsync(key);

        return Ok(result);
    }

    /// <summary>
    ///     刷新 Token 
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken()
    {
        var result = await authClient.RefreshSessionAsync();

        return Ok(result);
    }


    [HttpPost("logout")]
    public Task<IActionResult> LogOut()
    {
        authClient.LogOutAsync();
        return Task.FromResult<IActionResult>(Ok());
    }
}

// ================= DTO 模型 =================

public record MobileLoginRequest(string Mobile, string Code);