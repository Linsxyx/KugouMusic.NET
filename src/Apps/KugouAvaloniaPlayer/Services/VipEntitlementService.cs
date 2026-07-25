using System;
using System.Threading;
using System.Threading.Tasks;
using KuGou.Net.Clients;
using Microsoft.Extensions.Logging;
using ZLinq;

namespace KugouAvaloniaPlayer.Services;

public interface IVipEntitlementService
{
    Task<VipEntitlementResult> TryEnsureDailyVipAsync(CancellationToken cancellationToken = default);
}

internal sealed class VipEntitlementService(
    UserClient userClient,
    ILogger<VipEntitlementService> logger)
    : IVipEntitlementService
{
    public async Task<VipEntitlementResult> TryEnsureDailyVipAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var history = await userClient.GetVipRecordAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (history is not { Status: 1 })
        {
            logger.LogWarning("查询 VIP 领取记录失败: {ErrorCode}", history?.ErrorCode);
            return VipEntitlementResult.Failed(
                VipEntitlementFailureReason.HistoryUnavailable,
                history?.ErrorCode);
        }

        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var todayRecord = history.Items.AsValueEnumerable().FirstOrDefault(item => item.Day == today);
        switch (todayRecord)
        {
            case null:
                return await ReceiveAndUpgradeAsync(cancellationToken);
            case { VipType: "tvip" }:
                return await UpgradeAsync(cancellationToken);
            default:
                logger.LogInformation("今日 VIP 已领取");
                return VipEntitlementResult.SuccessResult;
        }
    }

    private async Task<VipEntitlementResult> ReceiveAndUpgradeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var receiveResult = await userClient.ReceiveOneDayVipAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var receiveSucceeded = receiveResult is { Status: 1 };
        if (receiveSucceeded)
            logger.LogInformation("VIP 领取成功");
        else
            logger.LogWarning("VIP 领取失败: {ErrorCode}", receiveResult?.ErrorCode);

        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        var upgradeResult = await userClient.UpgradeVipRewardAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (upgradeResult is not { Status: 1 })
            logger.LogWarning("VIP 升级失败: {ErrorCode}", upgradeResult?.ErrorCode);

        if (!receiveSucceeded)
            return VipEntitlementResult.Failed(
                VipEntitlementFailureReason.ReceiveFailed,
                receiveResult?.ErrorCode);

        return upgradeResult is { Status: 1 }
            ? VipEntitlementResult.SuccessResult
            : VipEntitlementResult.Failed(
                VipEntitlementFailureReason.UpgradeFailed,
                upgradeResult?.ErrorCode);
    }

    private async Task<VipEntitlementResult> UpgradeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await userClient.UpgradeVipRewardAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (result is { Status: 1 })
            return VipEntitlementResult.SuccessResult;

        logger.LogWarning("VIP 升级失败: {ErrorCode}", result?.ErrorCode);
        return VipEntitlementResult.Failed(
            VipEntitlementFailureReason.UpgradeFailed,
            result?.ErrorCode);
    }
}

public sealed record VipEntitlementResult(
    bool Success,
    VipEntitlementFailureReason FailureReason,
    int? ErrorCode)
{
    public static VipEntitlementResult SuccessResult { get; } =
        new(true, VipEntitlementFailureReason.None, null);

    public static VipEntitlementResult Failed(
        VipEntitlementFailureReason reason,
        int? errorCode) =>
        new(false, reason, errorCode);
}

public enum VipEntitlementFailureReason
{
    None,
    HistoryUnavailable,
    ReceiveFailed,
    UpgradeFailed
}
