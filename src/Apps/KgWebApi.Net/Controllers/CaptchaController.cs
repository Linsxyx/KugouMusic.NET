using KgWebApi.Net.Extensions;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("captcha")]
public class CaptchaController(LoginClient loginClient, VerificationClient verificationClient) : ControllerBase
{
    /// <summary>获取安全验证方式，eventId 来自响应中的 ssaCode。</summary>
    [HttpGet("verify/info")]
    public async Task<IActionResult> GetVerificationInfo([FromQuery][Required] string eventId) =>
        this.FromKgStatus(await verificationClient.GetInfoAsync(eventId));

    /// <summary>提交用户完成的安全验证，不自动重放原业务请求。</summary>
    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] SecurityVerificationRequest request) =>
        this.FromKgStatus(await verificationClient.SubmitAsync(request.EventId, request.VerificationType,
            request.VerifyCode, request.Sid, request.Edt));

    /// <summary>
    ///     发送验证码。
    /// </summary>
    /// <param name="mobile">手机号。</param>
    /// <returns>验证码发送结果。</returns>
    [HttpPost("sent")]
    [ProducesResponseType(typeof(SendCodeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SendCode([FromQuery][Required(AllowEmptyStrings = false)] string mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile) || mobile.Length < 11)
            return this.ApiBadRequest("手机号格式不正确", 40001);

        var result = await loginClient.SendCodeAsync(mobile);
        return this.FromKgStatus(result);
    }
}

public sealed record SecurityVerificationRequest(
    [Required] string EventId,
    [AllowedValues(23, 32)] int VerificationType,
    [Required] string VerifyCode,
    [Required] string Sid,
    [Required] string Edt);
