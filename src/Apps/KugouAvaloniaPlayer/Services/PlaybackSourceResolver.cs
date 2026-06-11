using System;
using System.Collections.Generic;
using System.IO;
using ZLinq;
using System.Threading;
using System.Threading.Tasks;
using KuGou.Net.Abstractions;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using KuGou.Net.Protocol.Session;
using KugouAvaloniaPlayer.Services.Jellyfin;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.Services;

public interface IPlaybackSourceResolver
{
    Task<PlaybackSourceResult> ResolveAsync(SongItem song, string quality, CancellationToken cancellationToken);
}

public sealed class PlaybackSourceResolver(
    SongClient songClient,
    KgSessionManager sessionManager,
    IJellyfinClient jellyfinClient,
    ILogger<PlaybackSourceResolver> logger)
    : IPlaybackSourceResolver
{
    public async Task<PlaybackSourceResult> ResolveAsync(
        SongItem song,
        string quality,
        CancellationToken cancellationToken)
    {
        var localFilePath = song.LocalFilePath;
        if (string.Equals(song.LocalSourceType, LocalMusicLibraryService.SourceTypeJellyfin, StringComparison.Ordinal) ||
            (!string.IsNullOrWhiteSpace(localFilePath) &&
             localFilePath.StartsWith("jellyfin://", StringComparison.OrdinalIgnoreCase)))
        {
            var streamUrl = ResolveJellyfinStreamUrl(song.RemoteUrl, localFilePath);
            return string.IsNullOrWhiteSpace(streamUrl)
                ? PlaybackSourceResult.Failed(PlaybackSourceFailureReason.Unavailable)
                : PlaybackSourceResult.Remote(streamUrl);
        }

        if (!string.IsNullOrWhiteSpace(localFilePath) && File.Exists(localFilePath))
            return PlaybackSourceResult.Local(localFilePath);

        if (string.IsNullOrEmpty(sessionManager.Session.Token) || sessionManager.Session.UserId == "0")
            return PlaybackSourceResult.Failed(PlaybackSourceFailureReason.LoginRequired);

        cancellationToken.ThrowIfCancellationRequested();
        var effectiveQuality = await ResolvePlaybackQualityAsync(song, quality, cancellationToken);
        var playData = await songClient.GetPlayInfoAsync(song.Hash, effectiveQuality, song.AlbumId);
        cancellationToken.ThrowIfCancellationRequested();

        if (playData == null || playData.Status != 1)
            return PlaybackSourceResult.Failed(PlaybackSourceFailureReason.Unavailable);

        var url = playData.Urls?.AsValueEnumerable().FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        return string.IsNullOrWhiteSpace(url)
            ? PlaybackSourceResult.Failed(PlaybackSourceFailureReason.EmptyUrl)
            : PlaybackSourceResult.Remote(url);
    }

    private async Task<string> ResolvePlaybackQualityAsync(
        SongItem song,
        string requestedQuality,
        CancellationToken cancellationToken)
    {
        var requestedRank = AudioQuality.GetRank(requestedQuality);
        if (requestedRank < 0 || string.IsNullOrWhiteSpace(song.Hash))
            return requestedQuality;

        var privileges = await songClient.GetPrivilegeLiteAsync(song.Hash, song.AlbumId);
        cancellationToken.ThrowIfCancellationRequested();
        if (privileges == null || privileges.Count == 0)
            return requestedQuality;

        var highestSupportedQualityRank = EnumerateSupportedQualities(privileges)
            .AsValueEnumerable().Select(AudioQuality.GetRank)
            .Where(rank => rank >= 0)
            .DefaultIfEmpty(-1)
            .Max();

        if (highestSupportedQualityRank < 0 || highestSupportedQualityRank >= requestedRank)
            return requestedQuality;

        var effectiveQuality = AudioQuality.Ordered[highestSupportedQualityRank];
        logger.LogInformation(
            "Playback quality downgraded for song {SongName} ({Hash}) from {RequestedQuality} to {EffectiveQuality}",
            song.Name,
            song.Hash,
            requestedQuality,
            effectiveQuality);

        return effectiveQuality;
    }

    private static IEnumerable<string> EnumerateSupportedQualities(IEnumerable<PrivilegeLiteData> privileges)
    {
        var stack = new Stack<PrivilegeLiteData>(privileges);
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.RelateGoods.Count > 0)
            {
                foreach (var relateGood in current.RelateGoods)
                    stack.Push(relateGood);
            }

            var normalizedQuality = AudioQuality.Normalize(current.Quality);
            if (!AudioQuality.IsKnown(normalizedQuality) || !yielded.Add(normalizedQuality))
                continue;

            yield return normalizedQuality;
        }
    }

    private static bool IsHttpUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private string? ResolveJellyfinStreamUrl(string? cachedStreamUrl, string? source)
    {
        if (!string.IsNullOrWhiteSpace(cachedStreamUrl))
            return cachedStreamUrl;

        if (IsHttpUrl(source))
            return source;

        return string.IsNullOrWhiteSpace(source)
            ? null
            : jellyfinClient.BuildStreamUrlFromPseudoPath(source);
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
