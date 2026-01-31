using System.Net;
using KuGou.Net.util;

// 引用你原来的工具类

namespace KuGou.Net.Protocol.Session;

public class KgSessionManager
{
    private readonly CookieContainer _cookieContainer;

    public KgSessionManager(CookieContainer cookieContainer)
    {
        _cookieContainer = cookieContainer;
        Session = KgSessionStore.Load() ?? new KgSession();

        // 1. 初始化 InstallGuid (如果为空)
        if (string.IsNullOrEmpty(Session.InstallGuid))
        {
            // JS 的 getGuid() 逻辑是生成一串随机 Hex
            // Guid.NewGuid().ToString("N") 生成的是 32位 Hex，完全符合要求
            Session.InstallGuid = Guid.NewGuid().ToString("N");
        }

        // 2. ★★★ 修正 MID 计算逻辑 ★★★
        // 原 JS: const mid = calculateMid(process.env.KUGOU_API_GUID ?? guid);
        // 只要 Guid 不变，Mid 就不变，和 Dfid 无关
        if (string.IsNullOrEmpty(Session.Mid) || Session.Mid == "-" || Session.Mid.Length < 30) // 简单的长度校验，防止旧算法生成的短MID
        {
            Session.Mid = KgUtils.CalcNewMid(Session.InstallGuid);
        }

        // 3. 初始化 DFID (如果为空)
        if (string.IsNullOrEmpty(Session.Dfid) || Session.Dfid == "-")
        {
            Session.Dfid = "-"; // 使用默认或随机
            // 注意：Session.Uuid 现在的计算方式可能也不重要了，JS里是 "-"，你也可以设为 "-"
            Session.Uuid = "-"; 
        }

        // ... 初始化 MAC, DEV 等 ...
        if (string.IsNullOrEmpty(Session.InstallMac)) Session.InstallMac = Guid.NewGuid().ToString("N");
        if (string.IsNullOrEmpty(Session.InstallDev)) Session.InstallDev = KgUtils.RandomString(16);

        KgSessionStore.Save(Session);
        SyncCookies();
    }

    public KgSession Session { get; }

    public void UpdateAuth(string userId, string token, string vipType, string vipToken ,string? t1)
    {
        Session.UserId = userId;
        Session.Token = token;
        Session.VipType = vipType;
        Session.VipToken = vipToken;
        Session.T1 = t1;

        KgSessionStore.Save(Session);
        SyncCookies();
    }

    private void SyncCookies()
    {
        SetCookie("userid", Session.UserId);
        SetCookie("token", Session.Token);
        SetCookie("vip_type", Session.VipType);
        SetCookie("vip_token", Session.VipToken);
    }

    private void SetCookie(string name, string value)
    {
        if (string.IsNullOrEmpty(name)) return;
        var domains = new[] { "kugou.com", "login-user.kugou.com", "gateway.kugou.com" };
        foreach (var domain in domains)
            try
            {
                _cookieContainer.Add(new Cookie(name, value ?? "", "/", domain));
            }
            catch
            {
            }
    }
}