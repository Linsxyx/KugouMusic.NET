using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("register")]
public class RegisterController(RegisterClient registerClient) : ControllerBase
{
    [HttpGet("dev")]
    public async Task<IActionResult> UserDetail()
    {
        var result = await registerClient.InitDeviceAsync();
        return Ok(result);
    }
}
