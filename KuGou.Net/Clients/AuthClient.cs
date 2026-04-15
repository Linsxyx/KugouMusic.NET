using KuGou.Net.Abstractions.Models;
using KuGou.Net.Adapters.Common;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using KuGou.Net.util;
using Microsoft.Extensions.Logging;

namespace KuGou.Net.Clients;

/// <summary>
///     认证客户端 - 不直接处理 JsonElement，统一使用 KgApiResponseParser 和强类型模型
/// </summary>
public class AuthClient(
    RawLoginApi rawApi,
    KgSessionManager sessionManager,
    ILogger<AuthClient> logger
)
{
    /// <summary>
    ///     发送验证码
    /// </summary>
    public async Task<SendCodeResponse?> SendCodeAsync(string mobile)
    {
        var json = await rawApi.SendSmsCodeAsync(mobile);
        return KgApiResponseParser.Parse<SendCodeResponse>(json, AppJsonContext.Default.SendCodeResponse);
    }

    /// <summary>
    ///     手机验证码登录并保存 Token
    /// </summary>
    public async Task<LoginResponse?> LoginByMobileAsync(string mobile, string code)
    {
        var json = await rawApi.LoginByMobileAsync(mobile, code);
        var data = KgApiResponseParser.Parse<LoginResponse>(json, AppJsonContext.Default.LoginResponse);
        if (data is not null && data.Status == 1)
        {
            var newToken = data.Token;
            var newUserId = data.UserId.ToString() ?? "";
            var t1 = data.T1;

            if (!string.IsNullOrEmpty(newToken))
            {
                sessionManager.UpdateAuth(newUserId, newToken, "0", "", t1);
                logger.LogInformation($"Token 登录成功! UserID: {newUserId}");
            }
        }
        else
        {
            logger.LogWarning("[Auth] 登录失败，若没有账号请先在酷狗音乐概念版App注册。");
        }

        return data;
    }

    /// <summary>
    ///     获取二维码 Key 和 URL
    /// </summary>
    public async Task<QRCode?> GetQrCodeAsync()
    {
        var json = await rawApi.GetQrKeyAsync();
        var data = KgApiResponseParser.Parse<QRCode>(json, AppJsonContext.Default.QRCode);
        return data;
    }

    /// <summary>
    ///     检查二维码扫码状态
    ///     返回: 0=等待, 1=已扫码, 2=过期, 4=登录成功,登录完记得刷下token拿t1
    /// </summary>
    public async Task<QrLoginStatusResponse?> CheckQrStatusAsync(string key)
    {
        var json = await rawApi.CheckQrStatusAsync(key);

        var res = KgApiResponseParser.Parse<QrLoginStatusResponse>(json, AppJsonContext.Default.QrLoginStatusResponse);

        if (res != null && res.IsSuccess)
        {
            var newUserId = res.UserId.ToString();
            var newToken = res.Token;

            sessionManager.UpdateAuth(newUserId!, newToken!, "0", "", "");

            logger.LogInformation($"二维码登录成功! UserID: {newUserId}, Nickname: {res.Nickname}");
        }
        else if (res != null && res.QrStatus == QrLoginStatus.Expired)
        {
            logger.LogWarning("[Auth] 二维码已过期，请重新获取。");
        }

        return res;
    }

    /// <summary>
    ///     刷新 Token (保活)
    /// </summary>
    public async Task<RefreshTokenResponse> RefreshSessionAsync()
    {
        var session = sessionManager.Session;

        // 没 Token 也没 UserID，跳过刷新
        if (string.IsNullOrEmpty(session.Token) || session.UserId == "0")
        {
            logger.LogError("[Auth] 本地无有效 Token，跳过刷新。");
            return new RefreshTokenResponse();
        }

        logger.LogInformation($"[Auth] 正在尝试刷新 Token (User: {session.UserId})...");


        var json = await rawApi.RefreshTokenAsync(session.UserId, session.Token, session.Dfid);

        var res = KgApiResponseParser.Parse<RefreshTokenResponse>(json, AppJsonContext.Default.RefreshTokenResponse);

        if (res?.Status == 1)
        {
            var newToken = res.Token;
            var newUserId = res.UserId.ToString();
            var vipType = res.IsVip.ToString();
            var t1 = res.T1;

            if (!string.IsNullOrEmpty(newToken))
            {
                sessionManager.UpdateAuth(newUserId, newToken, vipType, "", t1);
                logger.LogInformation($"Token 刷新成功! UserID: {newUserId}");
            }
        }

        return res ?? new RefreshTokenResponse();
    }


    //退出登录
    public void LogOutAsync()
    {
        sessionManager.Logout();
    }
}
