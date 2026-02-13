using System.Text.Json;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using KuGou.Net.util;

namespace KuGou.Net.Clients;

public class DeviceClient(RawDeviceApi rawApi, KgSessionManager sessionManager)
{
    public async Task<bool> InitDeviceAsync()
    {
        var session = sessionManager.Session;

        // 检查本地是否已有有效设备信息
        if (!string.IsNullOrEmpty(session.Dfid) && session.Dfid != "-")
            return true;

        Console.WriteLine("[Device] 检测到新设备，开始注册风控信息 (V2)...");
        return await RegisterDeviceAsync();
    }

    private async Task<bool> RegisterDeviceAsync()
    {
        var session = sessionManager.Session;
        
        // 调用新的 V2 接口
        // 此时 Session.InstallGuid 已经在 SessionManager 初始化时生成好了
        var json = await rawApi.RegisterDevAsync(session.UserId, session.Token);

        // 解析结果
        if (json.TryGetProperty("status", out var s) && s.GetInt32() == 1 &&
            json.TryGetProperty("data", out var data))
        {
            if (data.TryGetProperty("dfid", out var dfidElem))
            {
                var serverDfid = dfidElem.GetString();

                if (!string.IsNullOrEmpty(serverDfid))
                {
                    session.Dfid = serverDfid;
                    session.Mid = KgUtils.CalcNewMid(serverDfid);
                    session.Uuid = KgUtils.Md5(session.Dfid + session.Mid);

                    KgSessionStore.Save(session);
                    Console.WriteLine($"[Device] 注册成功! DFID: {serverDfid}");
                    return true;
                }
            }
        }
        Console.WriteLine("[Device] 注册失败。");
        return false;
    }
}