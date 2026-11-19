using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KuGou.Net.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using SimpleAudio;

namespace KugouAvaloniaPlayer.Services;

public interface IPlaybackCoordinator : IDisposable
{
    DualTrackAudioPlayer Player { get; }
    Task<bool> LoadAsync(string source, string songName, float normalizationGain, TimeSpan timeout,
        CancellationToken cancellationToken);
    Task<bool> PrepareNextAsync(string source, string songName, float normalizationGain, TimeSpan timeout,
        CancellationToken cancellationToken);
    void InvalidatePendingLoads();
}

public sealed class PlaybackCoordinator(ILogger<PlaybackCoordinator> logger) : IPlaybackCoordinator
{
    private readonly SemaphoreSlim _streamLoadGate = new(1, 1);
    private readonly HttpClient _remoteAudioClient = CreateRemoteAudioClient();
    private int _streamLoadOperationVersion;

    private static HttpClient CreateRemoteAudioClient()
    {
        var handler = KgPrimaryHandler.Create();
        handler.MaxResponseDrainSize = 0;
        handler.ResponseDrainTimeout = TimeSpan.Zero;
        return new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public DualTrackAudioPlayer Player { get; } = new();

    public async Task<bool> LoadAsync(
        string source,
        string songName,
        float normalizationGain,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await RunLoadAsync(
            source,
            songName,
            normalizationGain,
            timeout,
            prepareOnly: false,
            cancellationToken);
    }

    public async Task<bool> PrepareNextAsync(
        string source,
        string songName,
        float normalizationGain,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await RunLoadAsync(
            source,
            songName,
            normalizationGain,
            timeout,
            prepareOnly: true,
            cancellationToken);
    }

    private async Task<bool> RunLoadAsync(
        string source,
        string songName,
        float normalizationGain,
        TimeSpan timeout,
        bool prepareOnly,
        CancellationToken cancellationToken)
    {
        var operationVersion = Interlocked.Increment(ref _streamLoadOperationVersion);
        using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operationCts.CancelAfter(timeout);
        try
        {
            var loadTask = Task.Run(async () =>
            {
                await _streamLoadGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (operationVersion != Volatile.Read(ref _streamLoadOperationVersion) ||
                        operationCts.IsCancellationRequested)
                        return false;

                    var loaded = await LoadPlayerSourceAsync(
                        source,
                        normalizationGain,
                        prepareOnly,
                        operationCts.Token).ConfigureAwait(false);
                    if (!loaded)
                        return false;

                    if (operationVersion != Volatile.Read(ref _streamLoadOperationVersion) ||
                        operationCts.IsCancellationRequested)
                    {
                        if (prepareOnly)
                            Player.CancelPrepared();
                        else
                            Player.Stop();
                        return false;
                    }

                    return true;
                }
                finally
                {
                    _streamLoadGate.Release();
                }
            }, CancellationToken.None);

            var completed = await Task.WhenAny(loadTask, Task.Delay(timeout, cancellationToken));
            if (completed != loadTask)
            {
                operationCts.Cancel();
                if (cancellationToken.IsCancellationRequested) return false;
                InvalidatePendingLoads();
                logger.LogWarning(
                    prepareOnly ? "预加载歌曲超时: {SongName}, timeout={Timeout}s" : "加载歌曲超时: {SongName}, timeout={Timeout}s",
                    songName,
                    timeout.TotalSeconds);
                return false;
            }

            if (cancellationToken.IsCancellationRequested ||
                operationVersion != Volatile.Read(ref _streamLoadOperationVersion))
                return false;

            return await loadTask;
        }
        catch (OperationCanceledException)
        {
            InvalidatePendingLoads();
            return false;
        }
    }

    private async Task<bool> LoadPlayerSourceAsync(
        string source,
        float normalizationGain,
        bool prepareOnly,
        CancellationToken cancellationToken)
    {
        if (!IsHttpUrl(source))
            return prepareOnly
                ? Player.PrepareNext(source, normalizationGain)
                : Player.Load(source, normalizationGain);

        try
        {
            using var playbackSource = await RemoteAudioCache
                .OpenPlaybackSourceAsync(source, _remoteAudioClient, cancellationToken)
                .ConfigureAwait(false);
            var loaded = prepareOnly
                ? Player.PrepareNext(playbackSource, normalizationGain)
                : Player.Load(playbackSource, normalizationGain);
            if (loaded)
            {
                return true;
            }

            logger.LogWarning(
                ".NET 渐进式音频流无法交给 BASS，回退 BASS 直连: {Source}, Detail={Detail}",
                source,
                Player.LastErrorDetail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            logger.LogWarning(ex, ".NET 渐进式音频流建立失败，回退 BASS 直连: {Source}", source);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return prepareOnly
            ? Player.PrepareNext(source, normalizationGain)
            : Player.Load(source, normalizationGain);
    }

    private static bool IsHttpUrl(string source)
    {
        return Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public void InvalidatePendingLoads()
    {
        Interlocked.Increment(ref _streamLoadOperationVersion);
    }

    public void Dispose()
    {
        InvalidatePendingLoads();
        _streamLoadGate.Dispose();
        _remoteAudioClient.Dispose();
        Player.Dispose();
    }
}
