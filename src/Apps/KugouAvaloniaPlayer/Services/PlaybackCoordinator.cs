using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SimpleAudio;

namespace KugouAvaloniaPlayer.Services;

public interface IPlaybackCoordinator : IDisposable
{
    DualTrackAudioPlayer Player { get; }
    Task<bool> LoadAsync(string source, string songName, TimeSpan timeout, CancellationToken cancellationToken);
    void InvalidatePendingLoads();
}

public sealed class PlaybackCoordinator(ILogger<PlaybackCoordinator> logger) : IPlaybackCoordinator
{
    private readonly SemaphoreSlim _streamLoadGate = new(1, 1);
    private int _streamLoadOperationVersion;

    public DualTrackAudioPlayer Player { get; } = new();

    public async Task<bool> LoadAsync(
        string source,
        string songName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var operationVersion = Interlocked.Increment(ref _streamLoadOperationVersion);
        try
        {
            var loadTask = Task.Run(async () =>
            {
                await _streamLoadGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (operationVersion != Volatile.Read(ref _streamLoadOperationVersion) ||
                        cancellationToken.IsCancellationRequested)
                        return false;

                    var loaded = Player.Load(source);
                    if (!loaded)
                        return false;

                    if (operationVersion != Volatile.Read(ref _streamLoadOperationVersion) ||
                        cancellationToken.IsCancellationRequested)
                    {
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
                if (cancellationToken.IsCancellationRequested) return false;
                InvalidatePendingLoads();
                logger.LogWarning("加载歌曲超时: {SongName}, timeout={Timeout}s", songName, timeout.TotalSeconds);
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

    public void InvalidatePendingLoads()
    {
        Interlocked.Increment(ref _streamLoadOperationVersion);
    }

    public void Dispose()
    {
        InvalidatePendingLoads();
        _streamLoadGate.Dispose();
        Player.Dispose();
    }
}
