using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("captcha")]
public class CaptchaController(LoginClient loginClient) : ControllerBase
{
    [HttpPost("sent")]
    public async Task<IActionResult> SendCode(string mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile) || mobile.Length < 11)
            return BadRequest(new { status = 0, msg = "手机号格式不正确" });

        var result = await loginClient.SendCodeAsync(mobile);
        return Ok(result);
    }
}
