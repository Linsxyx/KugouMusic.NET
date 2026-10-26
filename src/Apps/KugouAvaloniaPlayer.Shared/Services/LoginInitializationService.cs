using System;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using KuGou.Net.Clients;
using KuGou.Net.Protocol.Session;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.Services;

public interface ILoginInitializationService
{
    Task<LoginInitializationResult> InitializeLocalSessionAsync();
    Task<UserProfileLoadResult> LoadCurrentUserProfileAsync();
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
