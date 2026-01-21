using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
[Route("[controller]")]
public class RegisterController(DeviceClient deviceClient) : ControllerBase
{
    [HttpGet("Dev")]
    public async Task<IActionResult> UserDetail()
    {
        var result = await deviceClient.InitDeviceAsync();
        return Ok(result);
    }
}