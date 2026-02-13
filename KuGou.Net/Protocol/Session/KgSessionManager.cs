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


        if (string.IsNullOrEmpty(Session.InstallGuid)) Session.InstallGuid = Guid.NewGuid().ToString("N");

        if (string.IsNullOrEmpty(Session.Mid) || Session.Mid == "-" || Session.Mid.Length < 30) // 简单的长度校验，防止旧算法生成的短MID
            Session.Mid = KgUtils.CalcNewMid(Session.InstallGuid);


        if (string.IsNullOrEmpty(Session.Dfid) || Session.Dfid == "-")
        {
            Session.Dfid = "-";

            Session.Uuid = "-";
        }

        if (string.IsNullOrEmpty(Session.InstallMac)) Session.InstallMac = Guid.NewGuid().ToString("N");
        if (string.IsNullOrEmpty(Session.InstallDev)) Session.InstallDev = KgUtils.RandomString();

        KgSessionStore.Save(Session);
        SyncCookies();
    }

    public KgSession Session { get; }

    public void UpdateAuth(string userId, string token, string vipType, string vipToken, string? t1)
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

    public void Logout()
    {
        KgSessionStore.Clear();

        ClearCookies();

        Session.UserId = "0";
        Session.Token = "";
        Session.VipType = "0";
        Session.VipToken = "";
        Session.T1 = "";
        Session.Dfid = "-";
        KgSessionStore.Save(Session);
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


    private void ClearCookies()
    {
        SetCookie("userid", "");
        SetCookie("token", "");
    }
}