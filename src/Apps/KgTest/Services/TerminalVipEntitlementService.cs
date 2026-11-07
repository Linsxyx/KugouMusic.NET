using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;

namespace KgTest.Services;

// 每日概念版 VIP 领取与升级逻辑，以及播放源获取失败时的账号保活+权益恢复，与 KugouAvaloniaPlayer 的 VipEntitlementService / PlaybackSourceRecoveryService 对齐。

internal enum TerminalVipFailureReason
{
    None,
    HistoryUnavailable,
    ReceiveFailed,
    UpgradeFailed
}

internal readonly record struct TerminalVipEntitlementResult(
    bool Success,
    TerminalVipFailureReason FailureReason,
    int? ErrorCode,
    string Summary)
{
    public static TerminalVipEntitlementResult Ok(string summary) =>
        new(true, TerminalVipFailureReason.None, null, summary);

    public static TerminalVipEntitlementResult Failed(
        TerminalVipFailureReason reason,
        int? errorCode,
        string summary) =>
        new(false, reason, errorCode, summary);
}

internal sealed class TerminalVipEntitlementService(TerminalKugouClients clients)
{
    private static readonly TimeSpan RecoveryCooldown = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _recoveryLock = new(1, 1);
    private DateTimeOffset _lastRecoveryAttemptUtc = DateTimeOffset.MinValue;
    private bool _lastRecoverySucceeded;

    /// <summary>
    ///     检查今日 VIP 领取记录：未领取则 ReceiveOneDayVipAsync + 延时 1s + UpgradeVipRewardAsync；已领 tvip 则仅 UpgradeVipRewardAsync。
    /// </summary>
    public async Task<TerminalVipEntitlementResult> TryEnsureDailyVipAsync(CancellationToken cancellationToken = default)
    {
        if (!clients.User.IsLoggedIn())
        {
            return TerminalVipEntitlementResult.Failed(TerminalVipFailureReason.HistoryUnavailable, null, "未登录");
        }

        VipReceiveHistoryResponse? history;
        try
        {
            history = await clients.User.GetVipRecordAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return TerminalVipEntitlementResult.Failed(
                TerminalVipFailureReason.HistoryUnavailable,
                null,
                "查询 VIP 领取记录异常");
        }

        if (history is not { Status: 1 })
        {
            return TerminalVipEntitlementResult.Failed(
                TerminalVipFailureReason.HistoryUnavailable,
                history?.ErrorCode,
                $"查询 VIP 记录失败 ({history?.ErrorCode})");
        }

        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var todayRecord = history.Items.FirstOrDefault(item => item.Day == today);
        return todayRecord switch
        {
            null => await ReceiveAndUpgradeAsync(cancellationToken),
            { VipType: "tvip" } => await UpgradeAsync(cancellationToken),
            _ => TerminalVipEntitlementResult.Ok("今日 VIP 已就绪")
        };
    }

    /// <summary>
    ///     当在线歌曲获取播放地址失败时，尝试 RefreshSessionAsync + TryEnsureDailyVipAsync（带 1 分钟冷却期），与 Avalonia 的 PlaybackSourceRecoveryService 一致。
    /// </summary>
    public async Task<bool> TryRecoverPlaybackAccountAsync(CancellationToken cancellationToken = default)
    {
        var session = clients.SessionManager.Session;
        if (string.IsNullOrWhiteSpace(session.Token) || session.UserId == "0")
        {
            return false;
        }

        await _recoveryLock.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _lastRecoveryAttemptUtc < RecoveryCooldown)
            {
                return _lastRecoverySucceeded;
            }

            _lastRecoveryAttemptUtc = now;
            _lastRecoverySucceeded = false;

            try
            {
                var refresh = await clients.Login.RefreshSessionAsync();
                if (refresh is not { Status: 1 })
                {
                    return false;
                }

                try
                {
                    await TryEnsureDailyVipAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // VIP 补领失败不阻断重试播放源。
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
            catch
            {
                return false;
            }
        }
        finally
        {
            _recoveryLock.Release();
        }
    }

    private async Task<TerminalVipEntitlementResult> ReceiveAndUpgradeAsync(CancellationToken cancellationToken)
    {
        var receiveResult = await clients.User.ReceiveOneDayVipAsync();
        var receiveSucceeded = receiveResult is { Status: 1 };

        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        var upgradeResult = await clients.User.UpgradeVipRewardAsync();

        if (!receiveSucceeded)
        {
            return TerminalVipEntitlementResult.Failed(
                TerminalVipFailureReason.ReceiveFailed,
                receiveResult?.ErrorCode,
                $"领取每日 VIP 失败 ({receiveResult?.ErrorCode})");
        }

        return upgradeResult is { Status: 1 }
            ? TerminalVipEntitlementResult.Ok("已自动领取并升级今日 VIP")
            : TerminalVipEntitlementResult.Failed(
                TerminalVipFailureReason.UpgradeFailed,
                upgradeResult?.ErrorCode,
                $"已领取一日 VIP，升级失败 ({upgradeResult?.ErrorCode})");
    }

    private async Task<TerminalVipEntitlementResult> UpgradeAsync(CancellationToken cancellationToken)
    {
        var result = await clients.User.UpgradeVipRewardAsync();
        return result is { Status: 1 }
            ? TerminalVipEntitlementResult.Ok("今日 VIP 已升级")
            : TerminalVipEntitlementResult.Failed(
                TerminalVipFailureReason.UpgradeFailed,
                result?.ErrorCode,
                $"VIP 升级失败 ({result?.ErrorCode})");
    }
}
