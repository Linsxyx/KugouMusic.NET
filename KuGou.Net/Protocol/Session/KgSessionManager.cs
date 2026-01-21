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

        if (Session.Dfid == "-" || string.IsNullOrEmpty(Session.Dfid))
        {
            Session.Dfid = KuGouConfig.Dfid;
            Session.Mid = KgUtils.CalcNewMid(Session.Dfid);
            Session.Uuid = KgUtils.Md5(Session.Dfid + Session.Mid);
        }

        SyncCookies();
    }

    public KgSession Session { get; }

    public void UpdateAuth(string userId, string token, string vipType, string vipToken)
    {
        Session.UserId = userId;
        Session.Token = token;
        Session.VipType = vipType;
        Session.VipToken = vipToken;

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