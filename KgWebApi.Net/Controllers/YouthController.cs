using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("[controller]")]
public class YouthController(UserClient userService) : ControllerBase
{
    [HttpGet("day/vip")]
    public async Task<IActionResult> OneDayVip()
    {
        var result = await userService.ReceiveOneDayVipAsync();
        return Ok(result);
    }

    [HttpGet("day/vip/upgrade")]
    public async Task<IActionResult> UpgradeVip()
    {
        var result = await userService.UpgradeVipRewardAsync();
        return Ok(result);
    }
}