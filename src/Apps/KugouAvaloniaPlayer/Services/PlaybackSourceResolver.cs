using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KuGou.Net.Clients;
using KuGou.Net.Protocol.Session;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services;

public interface IPlaybackSourceResolver
{
    Task<PlaybackSourceResult> ResolveAsync(SongItem song, string quality, CancellationToken cancellationToken);
}

public sealed class PlaybackSourceResolver(SongClient songClient, KgSessionManager sessionManager)
    : IPlaybackSourceResolver
{
    public async Task<PlaybackSourceResult> ResolveAsync(
        SongItem song,
        string quality,
        CancellationToken cancellationToken)
    {
        var localFilePath = song.LocalFilePath;
        if (!string.IsNullOrWhiteSpace(localFilePath) && File.Exists(localFilePath))
            return PlaybackSourceResult.Local(localFilePath);

        if (string.IsNullOrEmpty(sessionManager.Session.Token) || sessionManager.Session.UserId == "0")
            return PlaybackSourceResult.Failed(PlaybackSourceFailureReason.LoginRequired);

        cancellationToken.ThrowIfCancellationRequested();
        var playData = await songClient.GetPlayInfoAsync(song.Hash, quality);
        cancellationToken.ThrowIfCancellationRequested();

        if (playData == null || playData.Status != 1)
            return PlaybackSourceResult.Failed(PlaybackSourceFailureReason.Unavailable);

        var url = playData.Urls?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        return string.IsNullOrWhiteSpace(url)
            ? PlaybackSourceResult.Failed(PlaybackSourceFailureReason.EmptyUrl)
            : PlaybackSourceResult.Remote(url);
    }
}

public sealed record PlaybackSourceResult(
    bool Success,
    string? Source,
    bool IsLocal,
    PlaybackSourceFailureReason FailureReason)
{
    public static PlaybackSourceResult Local(string source) =>
        new(true, source, true, PlaybackSourceFailureReason.None);

    public static PlaybackSourceResult Remote(string source) =>
        new(true, source, false, PlaybackSourceFailureReason.None);

    public static PlaybackSourceResult Failed(PlaybackSourceFailureReason reason) =>
        new(false, null, false, reason);
}

public enum PlaybackSourceFailureReason
{
    None,
    LoginRequired,
    Unavailable,
    EmptyUrl
}
