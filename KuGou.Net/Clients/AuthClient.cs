using System.Text.Json;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Adapters.Common;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using KuGou.Net.util;

namespace KuGou.Net.Clients;

/// <summary>
///     认证客户端 - 不直接处理 JsonElement，统一使用 KgApiResponseParser 和强类型模型
/// </summary>
public class AuthClient(
    RawLoginApi rawApi,
    KgSessionManager sessionManager
//,ILogger<AuthClient> logger
)
{
    /// <summary>
    ///     发送验证码
    /// </summary>
    public async Task<JsonElement> SendCodeAsync(string mobile)
    {
        var json = await rawApi.SendSmsCodeAsync(mobile);
        return json;
    }

    /// <summary>
    ///     手机验证码登录并保存 Token
    /// </summary>
    public async Task<JsonElement> LoginByMobileAsync(string mobile, string code)
    {
        var json = await rawApi.LoginByMobileAsync(mobile, code);
        if (json.TryGetProperty("data", out var data))
        {
            var newToken = data.GetProperty("token").GetString();
            var newUserId = data.GetProperty("userid").ToString();
            //var vipType = data.GetProperty("is_vip").ToString();
            var t1 = data.GetProperty("t1").GetString();

            if (!string.IsNullOrEmpty(newToken))
            {
                sessionManager.UpdateAuth(newUserId, newToken, "0", "", t1);
                KgSessionStore.Save(sessionManager.Session);
                //logger.LogInformation($"Token 登录成功! UserID: {newUserId}");
            }

            //var newT1 = data.TryGetProperty("t1", out var t1El) ? t1El.GetString() : "";
        }

        //logger.LogWarning("[Auth] 登录 失败，返回数据中未找到 data 节点。");
        return json;
    }

    /// <summary>
    ///     获取二维码 Key 和 URL
    /// </summary>
    public async Task<JsonElement> GetQrCodeAsync()
    {
        var json = await rawApi.GetQrKeyAsync();
        return json;
    }

    /// <summary>
    ///     检查二维码扫码状态
    ///     返回: 0=等待, 1=已扫码, 2=过期, 4=登录成功,登录完记得刷下token拿t1
    /// </summary>
    public async Task<JsonElement> CheckQrStatusAsync(string key)
    {
        var json = await rawApi.CheckQrStatusAsync(key);

        if (json.TryGetProperty("data", out var data))
        {
            // 修正步骤 2: 从 data 节点中读取字段
            // 注意：userid 和 is_vip 是数字类型，使用 ToString() 最稳妥，能兼容数字和字符串
            var newToken = data.GetProperty("token").GetString();
            var newUserId = data.GetProperty("userid").ToString();

            if (!string.IsNullOrEmpty(newToken))
            {
                sessionManager.UpdateAuth(newUserId, newToken, "0", "", "");
                KgSessionStore.Save(sessionManager.Session);
                //logger.LogInformation($"Token 刷新成功! UserID: {newUserId}");
            }
        }

        //logger.LogWarning("[Auth] 刷新 Token 失败，返回数据中未找到 data 节点。");
        return json;
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
            Console.WriteLine("[Auth] 本地无有效 Token，跳过刷新。");
            return new RefreshTokenResponse();
        }

        Console.WriteLine($"[Auth] 正在尝试刷新 Token (User: {session.UserId})...");

        // 调用 Raw 接口
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
                KgSessionStore.Save(sessionManager.Session);
                //logger.LogInformation($"Token 刷新成功! UserID: {newUserId}");
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