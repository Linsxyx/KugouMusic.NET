using System;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using KuGou.Net.Clients;
using KuGou.Net.Protocol.Session;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;
using ZLinq;

namespace KugouAvaloniaPlayer.Services;

public interface ILoginInitializationService
{
    Task<LoginInitializationResult> InitializeLocalSessionAsync();
    Task<UserProfileLoadResult> LoadCurrentUserProfileAsync();
    Task<VipInitializationResult> TryReceiveStartupVipAsync();
}

internal sealed class LoginInitializationService(
    KgSessionManager sessionManager,
    LoginClient loginClient,
    UserClient userClient,
    ISukiToastManager toastManager, 
    ILogger<LoginInitializationService> logger) : ILoginInitializationService
{
    public async Task<LoginInitializationResult> InitializeLocalSessionAsync()
    {
        try
        {
            var session = sessionManager.Session;
            if (string.IsNullOrEmpty(session.Token))
            {
                logger.LogInformation("未登录，以游客身份运行。");
                loginClient.LogOutAsync();
                return LoginInitializationResult.GuestResult;
            }

            var profileResult = await LoadCurrentUserProfileAsync();
            logger.LogInformation("已加载本地用户: {UserId}", session.UserId);
            return new LoginInitializationResult(
                true,
                profileResult.Profile,
                profileResult.Failed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "登录初始化失败");
            await Dispatcher.UIThread.InvokeAsync(() =>
                toastManager.CreateToast()
                    .OfType(NotificationType.Error)
                    .WithTitle("登录初始化失败")
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .WithContent("登录初始化失败，请重新登录或检查网络连接")
                    .Queue());
            loginClient.LogOutAsync();
            return LoginInitializationResult.FailedResult;
        }
    }

    public async Task<UserProfileLoadResult> LoadCurrentUserProfileAsync()
    {
        try
        {
            var userInfo = await userClient.GetUserInfoAsync();
            if (userInfo == null)
                return UserProfileLoadResult.EmptyResult;

            return new UserProfileLoadResult(
                new UserProfileSnapshot(
                    userInfo.Name,
                    string.IsNullOrWhiteSpace(userInfo.Pic) ? null : userInfo.Pic,
                    sessionManager.Session.UserId),
                false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "加载用户信息失败");
            return UserProfileLoadResult.FailedResult;
        }
    }

    public async Task<VipInitializationResult> TryReceiveStartupVipAsync()
    {
        var history = await userClient.GetVipRecordAsync();
        if (history is not { Status: 1 })
        {
            logger.LogWarning("查询vip失败{ErrorCode}", history?.ErrorCode);
            await Dispatcher.UIThread.InvokeAsync(() =>
                toastManager.CreateToast()
                    .OfType(NotificationType.Warning)
                    .WithTitle("查询vip失败")
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .WithContent("请重新登录或检查网络连接")
                    .Queue());
            return new VipInitializationResult(false, history?.ErrorCode.ToString());
        }

        var todayStr = DateTime.Now.ToString("yyyy-MM-dd");
        var todayRecord = history.Items.AsValueEnumerable().FirstOrDefault(x => x.Day == todayStr);
        switch (todayRecord)
        {
            case null:
            {
                var data = await userClient.ReceiveOneDayVipAsync();
                if (data is not null && data.Status == 1)
                {
                    logger.LogInformation("vip领取成功");
                }

                else
                {
                    logger.LogError("vip领取失败{ErrorCode}", data?.ErrorCode);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        toastManager.CreateToast()
                            .OfType(NotificationType.Warning)
                            .WithTitle("领取vip失败")
                            .Dismiss().After(TimeSpan.FromSeconds(3))
                            .WithContent("请重新登录或在手机上手动领取")
                            .Queue());
                }

                await Task.Delay(1000);
                var data2 =await userClient.UpgradeVipRewardAsync();
                if (data2?.Status != 1)
                    logger.LogError("vip升级失败{ErrorCode}", data?.ErrorCode);
                break;
            }
            case { VipType: "tvip" }:
                await userClient.UpgradeVipRewardAsync();
                break;
            default:
                logger.LogInformation("今日已领取vip");
                break;
        }

        return VipInitializationResult.SuccessResult;
    }
}

public sealed record LoginInitializationResult(
    bool IsLoggedIn,
    UserProfileSnapshot? Profile,
    bool UserProfileLoadFailed)
{
    public static LoginInitializationResult GuestResult { get; } = new(false, null, false);
    public static LoginInitializationResult FailedResult { get; } = new(false, null, false);
}

public sealed record UserProfileLoadResult(
    UserProfileSnapshot? Profile,
    bool Failed)
{
    public static UserProfileLoadResult EmptyResult { get; } = new(null, false);
    public static UserProfileLoadResult FailedResult { get; } = new(null, true);
}

public sealed record UserProfileSnapshot(
    string UserName,
    string? UserAvatar,
    string UserId);

public sealed record VipInitializationResult(
    bool Success,
    string? ErrorCode)
{
    public static VipInitializationResult SuccessResult { get; } = new(true, null);
}
