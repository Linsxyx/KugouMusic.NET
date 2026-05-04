using System.Net;
using KuGou.Net.util;

namespace KuGou.Net.Protocol.Session;

public class KgSessionManager
{
    private readonly CookieContainer _cookieContainer;
    private readonly ISessionPersistence _sessionPersistence;

    public KgSessionManager(CookieContainer cookieContainer)
        : this(cookieContainer, new InMemorySessionPersistence())
    {
    }

    public KgSessionManager(CookieContainer cookieContainer, ISessionPersistence sessionPersistence)
    {
        _cookieContainer = cookieContainer;
        _sessionPersistence = sessionPersistence;
        Session = _sessionPersistence.Load() ?? new KgSession();


        if (string.IsNullOrEmpty(Session.InstallGuid)) Session.InstallGuid = Guid.NewGuid().ToString("N");

        if (string.IsNullOrEmpty(Session.Mid) || Session.Mid == "-" || Session.Mid.Length < 30) 
            Session.Mid = KgUtils.CalcNewMid(Session.InstallGuid);


        if (string.IsNullOrEmpty(Session.Dfid) || Session.Dfid == "-")
        {
            Session.Dfid = "-";

            Session.Uuid = "-";
        }

        if (string.IsNullOrEmpty(Session.InstallMac)) Session.InstallMac = Guid.NewGuid().ToString("N");
        if (string.IsNullOrEmpty(Session.InstallDev)) Session.InstallDev = KgUtils.RandomString();

        _sessionPersistence.Save(Session);
        SyncCookies();
    }

    public KgSession Session { get; }

    public void Persist()
    {
        _sessionPersistence.Save(Session);
    }

    public void UpdateAuth(string userId, string token, string vipType, string vipToken, string? t1)
    {
        Session.UserId = userId;
        Session.Token = token;
        Session.VipType = vipType;
        Session.VipToken = vipToken;
        Session.T1 = t1;

        _sessionPersistence.Save(Session);
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
        _sessionPersistence.Clear();

        ClearCookies();

        Session.UserId = "0";
        Session.Token = "";
        Session.VipType = "0";
        Session.VipToken = "";
        Session.T1 = "";
        Session.Dfid = "-";
        _sessionPersistence.Save(Session);
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
                // 
            }
    }


    private void ClearCookies()
    {
        SetCookie("userid", "");
        SetCookie("token", "");
    }
}
