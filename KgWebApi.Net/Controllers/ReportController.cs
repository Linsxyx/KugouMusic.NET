using KuGou.Net.Clients;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Controllers;

[ApiController]
public class ReportController(ReportClient reportClient) : ControllerBase
{
    [HttpPost("playhistory/upload")]
    public async Task<IActionResult> PlayHistoryUpload(
        [FromQuery(Name = "mxid")] long mixSongId,
        [FromQuery(Name = "time")] long? timestamp = null,
        [FromQuery(Name = "pc")] int playCount = 1)
    {
        var result = await reportClient.UploadPlayHistoryAsync(mixSongId, timestamp, playCount);
        return Ok(result);
    }

    [HttpPost("lastest/songs/listen")]
    public async Task<IActionResult> LatestSongsListen([FromQuery] int pagesize = 30)
    {
        var result = await reportClient.GetLatestSongsAsync(pagesize);
        return Ok(result);
    }

    [HttpPost("listen/timeadd")]
    public async Task<IActionResult> ListenTimeAdd()
    {
        var result = await reportClient.AddListenTimeAsync();
        return Ok(result);
    }
}
