namespace KgWebApi.Net.Data.Entities;

public sealed class AppVersionEntity
{
    public int Id { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string VersionName { get; set; } = string.Empty;
    public int VersionCode { get; set; }
    public string UpdateContent { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public bool ForceUpdate { get; set; }
    public DateTimeOffset ReleaseDate { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
