using System.Text;
using System.Text.Json;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

// 引用原有工具类

namespace KuGou.Net.Protocol.Raw;

public class RawDeviceApi(IKgTransport transport)
{
    private const string RegisterSalt = "1014";

    /// <summary>
    ///     注册设备获取 DFID
    /// </summary>
    public async Task<JsonElement> RegisterDevAsync(
        string userId,
        string token,
        string tempDfid,
        string localMid,
        string clientTime)
    {
        var paramsDict = new Dictionary<string, string>
        {
            { "appid", "1014" },
            { "clientver", KuGouConfig.ClientVer },
            { "clienttime", clientTime },
            { "dfid", tempDfid },
            { "mid", "" }, // 必须传空
            { "uuid", "" }, // 必须传空
            { "userid", userId },
            { "p.token", "" }, // 必须传空
            { "platid", "4" }
        };

        if (!string.IsNullOrEmpty(token)) paramsDict["token"] = token;

        paramsDict["signature"] = CalcRegisterSignature(paramsDict);

        // 3. 准备 Body (JSON -> Base64)
        var bodyObj = new Dictionary<string, string>
        {
            ["mid"] = "",
            ["uuid"] = "",
            ["appid"] = "1014",
            ["userid"] = userId
        };
        var bodyJson = JsonSerializer.Serialize(bodyObj);
        var bodyBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(bodyJson));

        // 4. 构造请求
        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            BaseUrl = "https://userservice.kugou.com",
            Path = "/risk/v1/r_register_dev",
            Params = paramsDict,
            RawBody = bodyBase64,
            ContentType = "text/plain",
            SignatureType = SignatureType.None
        };


        request.SpecificDfid = tempDfid;

        return await transport.SendAsync(request);
    }

    private static string CalcRegisterSignature(Dictionary<string, string> paramsDict)
    {
        var values = new List<string>();
        foreach (var kv in paramsDict) values.Add(kv.Value);

        values.Sort();

        var valuesString = string.Join("", values);

        return KgUtils.Md5($"{RegisterSalt}{valuesString}{RegisterSalt}");
    }
}