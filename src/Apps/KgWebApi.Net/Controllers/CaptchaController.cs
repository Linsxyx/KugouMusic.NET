using KgWebApi.Net.Extensions;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("captcha")]
public class CaptchaController(LoginClient loginClient) : ControllerBase
{
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
