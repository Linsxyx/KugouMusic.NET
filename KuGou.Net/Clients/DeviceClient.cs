using System.Text.Json;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using KuGou.Net.util;

namespace KuGou.Net.Clients;

public class DeviceClient(RawDeviceApi rawApi, KgSessionManager sessionManager)
{
    /// <summary>
    ///     初始化设备 (如果本地没有 DFID 则注册，有则跳过)
    /// </summary>
    public async Task<bool> InitDeviceAsync()
    {
        var session = sessionManager.Session;

        // 1. 检查本地是否已有有效设备信息
        if (!string.IsNullOrEmpty(session.Dfid) && session.Dfid != "-" &&
            !string.IsNullOrEmpty(session.Mid))
            //Console.WriteLine($"[Device] 已有设备信息 DFID: {session.Dfid}");
            return true;

        //Console.WriteLine("[Device] 检测到新设备，开始注册风控信息...");
        return await RegisterDeviceAsync();
    }

    private async Task<bool> RegisterDeviceAsync()
    {
        var session = sessionManager.Session;

        // 1. 生成临时 ID
        var registerDfid = KgUtils.RandomString(24);
        var clientTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        // 2. 预计算真实的 Mid/Uuid (保存用)
        var localMid = KgUtils.CalcNewMid(registerDfid); // 使用工具类算法
        //var localUuid = KgUtils.Md5(registerDfid + localMid);

        // 3. 调用 Raw API
        var json = await rawApi.RegisterDevAsync(
            session.UserId,
            session.Token,
            registerDfid,
            localMid,
            clientTime
        );

        // 4. 解析结果
        if (json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty("status", out var statusElem) &&
            statusElem.GetInt32() == 1)
            if (json.TryGetProperty("data", out var dataElem) &&
                dataElem.ValueKind == JsonValueKind.Object &&
                dataElem.TryGetProperty("dfid", out var dfidElem))
            {
                var serverDfid = dfidElem.GetString();

                if (!string.IsNullOrEmpty(serverDfid))
                {
                    session.Dfid = serverDfid;
                    session.Mid = KgUtils.CalcNewMid(serverDfid);
                    session.Uuid = KgUtils.Md5(session.Dfid + session.Mid);

                    KgSessionStore.Save(session);
                    return true;
                }
            }

        return false;
    }
}