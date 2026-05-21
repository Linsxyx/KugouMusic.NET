using KgWebApi.Net.Data;
using KgWebApi.Net.Data.Entities;
using KgWebApi.Net.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace KgWebApi.Net.Controllers.MobileApp;

/// <summary>
///     移动端 App 信息接口。
/// </summary>
[ApiController]
[Route("mobile/app")]
public class ApplicationInfo(KgWebApiDbContext dbContext) : ControllerBase
{
    /// <summary>
    ///     获取所有版本信息，可按平台过滤。
    /// </summary>
    /// <param name="platform">平台，可选值：Android、iOS。</param>
    /// <returns>版本信息列表。</returns>
    [HttpGet("versions")]
    [ProducesResponseType(typeof(List<AppVersionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVersions([FromQuery] string? platform = null)
    {
        IQueryable<AppVersionEntity> query = dbContext.AppVersions;

        if (!string.IsNullOrWhiteSpace(platform))
            query = query.Where(v => v.Platform == platform);

        var versions = await query
            .OrderByDescending(v => v.VersionCode)
            .Select(v => new AppVersionDto(
                v.Platform,
                v.VersionName,
                v.VersionCode,
                v.UpdateContent,
                v.DownloadUrl,
                v.ForceUpdate,
                v.ReleaseDate
            ))
            .ToListAsync();

        return Ok(versions);
    }

    /// <summary>
    ///     获取指定平台的最新版本信息。
    /// </summary>
    /// <param name="platform">平台，可选值：Android、iOS。</param>
    /// <returns>最新版本信息，若未找到则返回 404。</returns>
    [HttpGet("versions/latest")]
    [ProducesResponseType(typeof(AppVersionDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLatestVersion([FromQuery][Required(AllowEmptyStrings = false)] string platform)
    {
        var version = await dbContext.AppVersions
            .Where(v => v.Platform == platform)
            .OrderByDescending(v => v.VersionCode)
            .Select(v => new AppVersionDto(
                v.Platform,
                v.VersionName,
                v.VersionCode,
                v.UpdateContent,
                v.DownloadUrl,
                v.ForceUpdate,
                v.ReleaseDate
            ))
            .FirstOrDefaultAsync();

        if (version is null)
            return this.ApiNotFound($"未找到平台 {platform} 的版本信息");

        return Ok(version);
    }
}

/// <summary>
///     App 版本信息。
/// </summary>
public sealed record AppVersionDto(
    string Platform,
    string VersionName,
    int VersionCode,
    string UpdateContent,
    string DownloadUrl,
    bool ForceUpdate,
    DateTimeOffset ReleaseDate
);
