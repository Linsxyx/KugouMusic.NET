using System.Text.Json;
using System.Text.Json.Nodes;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Session;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

namespace KuGou.Net.Protocol.Raw;

public class RawLoginApi(IKgTransport transport ,KgSessionManager sessionManager)
{
    private const string LiteT1Key = "5e4ef500e9597fe004bd09a46d8add98";
    private const string LiteT1Iv = "04bd09a46d8add98";
    
    private const string LiteT2Key = "fd14b35e3f81af3817a20ae7adae7020";
    private const string LiteT2Iv = "17a20ae7adae7020";
    
    // T2 加密中间的一个固定 Hash，来自 JS 源码
    private const string T2FixedHash = "0f607264fc6318a92b9e13c65db7cd3c";
    
    // 3116 专用的 LiteKey (用于 Token 刷新时的 P3 加密)
    private const string LiteAppKey = "c24f74ca2820225badc01946dba4fdf7";
    private const string LiteAppIv = "adc01946dba4fdf7";

    private const string ApiHost = "http://login.user.kugou.com";
    private const string LoginRouter = "login.user.kugou.com";
    private const string LoginRetryHost = "https://loginserviceretry.kugou.com"; // v7 登录通常用这个
    private const string WebHost = "https://login-user.kugou.com";

    /// <summary>
    ///     手机验证码登录 (对应原 LoginByMobileAsync)
    /// </summary>
    public async Task<JsonElement> LoginByMobileAsync(string mobile, string code)
    {
        var session = sessionManager.Session;
        var dateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds(); // ms
        
        // 1. T1 计算: AES(|timestamp)
        // JS: const t1 = cryptoAesEncrypt(`|${dateTime}`, ...)
        var t1Raw = $"|{dateTime}";
        var (t1Enc, _) = KgCrypto.AesEncrypt(t1Raw, LiteT1Key, LiteT1Iv);

        // 2. T2 计算: AES(GUID|Fixed|MAC|DEV|timestamp)
        // JS: `${params.cookie?.KUGOU_API_GUID}|...|${params.cookie?.KUGOU_API_DEV}|${dateTime}`
        var t2Raw = $"{session.InstallGuid}|{T2FixedHash}|{session.InstallMac}|{session.InstallDev}|{dateTime}";
        var (t2Enc, _) = KgCrypto.AesEncrypt(t2Raw, LiteT2Key, LiteT2Iv);

        // 3. 常规 AES 加密 params
        var aesPayload = new JsonObject
        {
            ["mobile"] = mobile,
            ["code"] = code
        };
        var aesJson = JsonSerializer.Serialize(aesPayload, AppJsonContext.Default.JsonObject);
        var (aesStr, aesKey) = KgCrypto.AesEncrypt(aesJson); // 随机 Key

        // 4. RSA 加密 Key
        var pkData = new JsonObject
        {
            ["clienttime_ms"] = dateTime,
            ["key"] = aesKey
        };
        var pkJson = JsonSerializer.Serialize(pkData, AppJsonContext.Default.JsonObject);
        var pk = KgCrypto.RsaEncryptNoPadding(pkJson).ToUpper();

        // 5. 组装 Body
        var maskedMobile = mobile.Length > 10
            ? $"{mobile[..2]}*****{mobile.Substring(10, 1)}"
            : mobile;

        var dataMap = new JsonObject
        {
            ["plat"] = 1,
            ["support_multi"] = 1,
            ["t1"] = t1Enc,
            ["t2"] = t2Enc,
            ["clienttime_ms"] = dateTime,
            ["mobile"] = maskedMobile,
            ["key"] = KgSigner.CalcLoginKey(dateTime),
            ["pk"] = pk,
            ["params"] = aesStr,
            
            // Lite 版新增参数
            //["dfid"] = session.Dfid,
            ["dfid"] = "-",
            ["dev"] = session.InstallDev,
            ["gitversion"] = "5f0b7c4" 
        };
        
        if (session.UserId != "0") dataMap["userid"] = session.UserId;

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            BaseUrl = LoginRetryHost, // 切换到 retry 域名
            Path = "/v7/login_by_verifycode",
            SpecificRouter = LoginRouter, // 路由还是保持 login.user
            Body = dataMap,
            SignatureType = SignatureType.Default,
            CustomHeaders = new Dictionary<string, string>
            {
                { "support-calm", "1" }
            }
        };

        var response = await transport.SendAsync(request);

        // 解密响应逻辑 (复用)
        return TryDecryptResponse(response, aesKey);
    }

    /// <summary>
    ///     发送验证码
    /// </summary>
    public async Task<JsonElement> SendSmsCodeAsync(string mobile)
    {
        var body = new JsonObject
        {
            ["businessid"] = 5,
            ["mobile"] = mobile,
            ["plat"] = 3
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            BaseUrl = ApiHost,
            Path = "/v7/send_mobile_code",
            SpecificRouter = LoginRouter,
            Body = body,
            SignatureType = SignatureType.Default
        };

        return await transport.SendAsync(request);
    }


    /// <summary>
    ///     A. 获取二维码及 Key
    /// </summary>
    public async Task<JsonElement> GetQrKeyAsync()
    {
        var paramsDict = new Dictionary<string, string>
        {
            { "appid", "1001" }, // Web 端 AppId
            { "clientver", "11040" },
            { "type", "1" },
            { "plat", "4" },
            { "srcappid", "2919" },
            { "qrcode_txt", "https://h5.kugou.com/apps/loginQRCode/html/index.html?appid=3116&" }
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Get,
            BaseUrl = WebHost,
            Path = "/v2/qrcode",
            Params = paramsDict,
            SignatureType = SignatureType.Web
        };

        return await transport.SendAsync(request);
    }

    /// <summary>
    ///     B. 检查二维码状态 (轮询用)
    /// </summary>
    public async Task<JsonElement> CheckQrStatusAsync(string key)
    {
        var paramsDict = new Dictionary<string, string>
        {
            { "plat", "4" },
            { "appid", "3116" },
            { "srcappid", "2919" },
            { "qrcode", key }
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Get,
            BaseUrl = WebHost,
            Path = "/v2/get_userinfo_qrcode",
            Params = paramsDict,
            SignatureType = SignatureType.Web
        };

        return await transport.SendAsync(request);
    }


    /// <summary>
    ///     刷新 Token (核心逻辑搬运)
    /// </summary>
    public async Task<JsonElement> RefreshTokenAsync(string userid, string token, string dfid)
    {
        var session = sessionManager.Session;
        var dateNow = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        var clienttimeSec = dateNow / 1000;

        // 1. T1 计算
        var lastT1 = session.T1;
        var t1Raw = string.IsNullOrEmpty(lastT1) 
            ? $"|{dateNow}" 
            : $"{lastT1}|{dateNow}"; 
        var (t1Enc, _) = KgCrypto.AesEncrypt(t1Raw, LiteT1Key, LiteT1Iv);

        // 2. T2 计算 (同登录)
        var t2Raw = $"{session.InstallGuid}|{T2FixedHash}|{session.InstallMac}|{session.InstallDev}|{dateNow}";
        var (t2Enc, _) = KgCrypto.AesEncrypt(t2Raw, LiteT2Key, LiteT2Iv);

        // 3. P3 加密 (Token + ClientTime)
        var p3Data = new JsonObject
        {
            ["clienttime"] = clienttimeSec,
            ["token"] = token
        };
        var p3Json = JsonSerializer.Serialize(p3Data, AppJsonContext.Default.JsonObject);
        var (p3Encrypted, _) = KgCrypto.AesEncrypt(p3Json, LiteAppKey, LiteAppIv);

        // 4. Params 加密 (这里生成一个随机 Key 用于解密返回值)
        var (paramsEncrypted, randomAesKey) = KgCrypto.AesEncrypt("{}"); // params 为空对象

        // 5. PK 加密
        var pkPayload = new JsonObject
        {
            ["clienttime_ms"] = dateNow,
            ["key"] = randomAesKey // 告诉服务器用这个 Key 加密返回数据
        };
        var pkJson = JsonSerializer.Serialize(pkPayload, AppJsonContext.Default.JsonObject);
        var pk = KgCrypto.RsaEncryptNoPadding(pkJson).ToUpper();

        // 6. 组装 Body
        var body = new JsonObject
        {
            ["dfid"] = string.IsNullOrEmpty(dfid) ? "-" : "-",
            ["p3"] = p3Encrypted,
            ["plat"] = 1,
            ["t1"] = t1Enc,
            ["t2"] = t2Enc,
            ["t3"] = "MCwwLDAsMCwwLDAsMCwwLDA=", // 固定值
            ["pk"] = pk,
            ["params"] = paramsEncrypted,
            ["userid"] = userid,
            ["clienttime_ms"] = dateNow,
            ["dev"] = session.InstallDev // JS 新增字段
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            BaseUrl = ApiHost,
            Path = "/v5/login_by_token", 
            SpecificRouter = LoginRouter,
            Body = body,
            SignatureType = SignatureType.Default
        };

        var response = await transport.SendAsync(request);

        // 使用 randomAesKey 解密返回的 secu_params
        return TryDecryptResponse(response, randomAesKey);
    }
    
    
    private JsonElement TryDecryptResponse(JsonElement response, string aesKey)
    {
        try
        {
            // 简单判断状态
            if (response.TryGetProperty("status", out var s) && s.GetInt32() == 1 &&
                response.TryGetProperty("data", out var dataElem))
            {
                if (dataElem.TryGetProperty("secu_params", out var secuElem))
                {
                    // 解密
                    var decryptedJson = KgCrypto.AesDecrypt(secuElem.GetString()!, aesKey);
                    
                    // 这里我们需要把解密出来的字段合并回 data 节点
                    // 因为 System.Text.Json 的 JsonElement 是只读的，所以需要重建 JsonObject
                    
                    var rootNode = JsonNode.Parse(response.GetRawText());
                    var dataNode = rootNode?["data"] as JsonObject;
                    var decryptedNode = JsonNode.Parse(decryptedJson) as JsonObject;
                    
                    if(dataNode != null && decryptedNode != null)
                    {
                        foreach(var kv in decryptedNode)
                        {
                            // 覆盖或添加字段 (token, t1, userid 等)
                            dataNode[kv.Key] = kv.Value?.DeepClone();
                        }
                        return JsonSerializer.Deserialize<JsonElement>(rootNode!.ToJsonString(), AppJsonContext.Default.JsonElement);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RawLoginApi] 解密响应失败: {ex.Message}");
        }
        return response;
    }
}