using System;
using System.Threading;
using System.Threading.Tasks;
using KuGou.Net.Clients;
using KuGou.Net.Protocol.Session;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.Services;

public interface IPlaybackSourceRecoveryService
{
    Task<PlaybackSourceResult> ResolveWithRecoveryAsync(
        SongItem song,
        string quality,
        CancellationToken cancellationToken);
}

internal sealed class PlaybackSourceRecoveryService(
    IPlaybackSourceResolver sourceResolver,
    KgSessionManager sessionManager,
    LoginClient loginClient,
    IVipEntitlementService vipEntitlementService,
    ILogger<PlaybackSourceRecoveryService> logger)
    : IPlaybackSourceRecoveryService
{
    private static readonly TimeSpan RecoveryCooldown = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _recoveryLock = new(1, 1);
    private DateTimeOffset _lastRecoveryAttemptUtc = DateTimeOffset.MinValue;
    private bool _lastRecoverySucceeded;

    public async Task<PlaybackSourceResult> ResolveWithRecoveryAsync(
        SongItem song,
        string quality,
        CancellationToken cancellationToken)
    {
        var initialResult = await ResolveSafelyAsync(song, quality, cancellationToken);
        if (initialResult.Success || !CanAttemptRecovery(initialResult))
            return initialResult;

        var recoverySucceeded = await TryRecoverAccountAsync(initialResult.Origin, cancellationToken);
        if (!recoverySucceeded)
            return initialResult;

        logger.LogInformation(
            "账号状态恢复完成，重试播放源解析: {SongName}, Origin={Origin}",
            song.Name,
            initialResult.Origin);
        return await ResolveSafelyAsync(song, quality, cancellationToken);
    }

    private async Task<PlaybackSourceResult> ResolveSafelyAsync(
        SongItem song,
        string quality,
        CancellationToken cancellationToken)
    {
        try
        {
            return await sourceResolver.ResolveAsync(song, quality, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var origin = GetExpectedOrigin(song);
            logger.LogWarning(
                ex,
                "播放源解析抛出异常: {SongName}, Origin={Origin}",
                song.Name,
                origin);
            return PlaybackSourceResult.Failed(PlaybackSourceFailureReason.Unavailable, origin);
        }
    }

    private static PlaybackSourceOrigin GetExpectedOrigin(SongItem song)
    {
        if (string.Equals(
                song.LocalSourceType,
                LocalMusicLibraryService.SourceTypeJellyfin,
                StringComparison.Ordinal) ||
            (!string.IsNullOrWhiteSpace(song.LocalFilePath) &&
             song.LocalFilePath.StartsWith("jellyfin://", StringComparison.OrdinalIgnoreCase)))
        {
            return PlaybackSourceOrigin.Jellyfin;
        }

        return song.PlaybackSource == SongPlaybackSource.UserCloud
            ? PlaybackSourceOrigin.KugouUserCloud
            : PlaybackSourceOrigin.KugouCatalog;
    }

    private bool CanAttemptRecovery(PlaybackSourceResult result)
    {
        if (result.Origin is not (PlaybackSourceOrigin.KugouCatalog or PlaybackSourceOrigin.KugouUserCloud))
            return false;

        var session = sessionManager.Session;
        return !string.IsNullOrWhiteSpace(session.Token) && session.UserId != "0";
    }

    private async Task<bool> TryRecoverAccountAsync(
        PlaybackSourceOrigin origin,
        CancellationToken cancellationToken)
    {
        await _recoveryLock.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _lastRecoveryAttemptUtc < RecoveryCooldown)
            {
                logger.LogDebug(
                    "账号恢复仍在冷却期内，复用上次结果: Success={Success}",
                    _lastRecoverySucceeded);
                return _lastRecoverySucceeded;
            }

            _lastRecoveryAttemptUtc = now;
            _lastRecoverySucceeded = false;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var refreshResult = await loginClient.RefreshSessionAsync();
                cancellationToken.ThrowIfCancellationRequested();
                if (refreshResult is not { Status: 1 })
                {
                    logger.LogWarning(
                        "播放源恢复时刷新 Session 失败: {ErrorCode}",
                        refreshResult.ErrorCode);
                    return false;
                }

                if (origin == PlaybackSourceOrigin.KugouCatalog)
                {
                    try
                    {
                        var vipResult =
                            await vipEntitlementService.TryEnsureDailyVipAsync(cancellationToken);
                        if (!vipResult.Success)
                        {
                            logger.LogWarning(
                                "播放源恢复时更新 VIP 权益未完全成功: Reason={Reason}, ErrorCode={ErrorCode}",
                                vipResult.FailureReason,
                                vipResult.ErrorCode);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "播放源恢复时更新 VIP 权益失败，继续重试播放源");
                    }
                }

                _lastRecoverySucceeded = true;
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _lastRecoveryAttemptUtc = DateTimeOffset.MinValue;
                _lastRecoverySucceeded = false;
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "播放源账号恢复失败");
                return false;
            }
        }
        finally
        {
            _recoveryLock.Release();
        }
    }
}
