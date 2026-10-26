namespace KugouAvaloniaPlayer.Services.Jellyfin;

public sealed record JellyfinConnectionOptions(
    string ServerUrl,
    string UserId,
    string ApiKey);

public sealed record JellyfinLibrary(
    string Id,
    string Name);

public sealed record JellyfinAudioItem(
    string Id,
    string Name,
    string Artist,
    string AlbumId,
    string Album,
    double DurationSeconds,
    string? CoverUrl,
    string StreamUrl);

public sealed class JellyfinImportProgress
{
    public int Processed { get; init; }
    public int Total { get; init; }
    public double Percentage => Total <= 0 ? 0 : System.Math.Clamp(Processed * 100.0 / Total, 0, 100);
    public string Message { get; init; } = string.Empty;
}
