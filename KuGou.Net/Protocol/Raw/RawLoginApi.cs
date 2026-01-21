using System.Text.Json;
using System.Text.Json.Nodes;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

namespace KuGou.Net.Protocol.Raw;

public class RawLoginApi(IKgTransport transport)
{
    private const string ApiHost = "http://login.user.kugou.com";
    private const string LoginRouter = "login.user.kugou.com";
    private const string WebHost = "https://login-user.kugou.com";

    /// <summary>
    ///     手机验证码登录 (对应原 LoginByMobileAsync)
    /// </summary>
    public async Task<JsonElement> LoginByMobileAsync(string mobile, string code)
    {
        var isLite = true;
        var dateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        var aesPayload = new JsonObject
        {
            ["mobile"] = mobile,
            ["code"] = code
        };
        var aesJson = JsonSerializer.Serialize(aesPayload, AppJsonContext.Default.JsonObject);
        var (aesStr, aesKey) = KgCrypto.AesEncrypt(aesJson);

        var dataMap = new JsonObject
        {
            ["plat"] = 1,
            ["support_multi"] = 1,
            ["t1"] = 0,
            ["t2"] = 0,
            ["clienttime_ms"] = dateTime,
            ["key"] = KgSigner.CalcLoginKey(dateTime) // 原有的 Signer
        };

        if (isLite)
        {
            dataMap["mobile"] = mobile;
            var p2Data = new JsonObject
            {
                ["clienttime_ms"] = dateTime,
                ["code"] = code,
                ["mobile"] = mobile
            };
            var p2Json = JsonSerializer.Serialize(p2Data, AppJsonContext.Default.JsonObject);
            dataMap["p2"] = KgCrypto.RsaEncryptNoPadding(p2Json).ToUpper();
        }
        else
        {
            var maskedMobile = mobile.Length > 10
                ? $"{mobile[..2]}*****{mobile.Substring(10, 1)}"
                : mobile;

            dataMap["mobile"] = maskedMobile;
            dataMap["t3"] = "MCwwLDAsMCwwLDAsMCwwLDA=";
            dataMap["params"] = aesStr;

            var pkData = new JsonObject
            {
                ["clienttime_ms"] = dateTime,
                ["key"] = aesKey
            };
            dataMap["pk"] = KgCrypto.RsaEncryptNoPadding(JsonSerializer.Serialize(pkData))
                .ToUpper();
        }

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            BaseUrl = ApiHost,
            Path = isLite ? "/v6/login_by_verifycode" : "/v7/login_by_verifycode",
            SpecificRouter = LoginRouter,
            Body = dataMap,
            SignatureType = SignatureType.Default
        };

        var response = await transport.SendAsync(request);

        string? decrypted = null;

        if (response.TryGetProperty("data", out var dataElem)
            && dataElem.ValueKind == JsonValueKind.Object
            && dataElem.TryGetProperty("secu_params", out var secuElem)
            && secuElem.ValueKind == JsonValueKind.String)
            try
            {
                decrypted = KgCrypto.AesDecrypt(secuElem.GetString()!, aesKey);
            }
            catch
            {
                // log
            }

        return response;
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
        const string fixedKey = "c24f74ca2820225badc01946dba4fdf7";
        const string fixedIv = "adc01946dba4fdf7";

        var dateNow = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        var clienttimeSec = dateNow / 1000;

        var p3Data = new JsonObject
        {
            ["clienttime"] = clienttimeSec,
            ["token"] = token
        };
        var p3Json = JsonSerializer.Serialize(p3Data, AppJsonContext.Default.JsonObject);
        var (p3Encrypted, _) = KgCrypto.AesEncrypt(p3Json, fixedKey, fixedIv);

        var (paramsEncrypted, randomAesKey) = KgCrypto.AesEncrypt("{}");

        var pkPayload = new JsonObject
        {
            ["clienttime_ms"] = dateNow,
            ["key"] = randomAesKey
        };
        var pkJson = JsonSerializer.Serialize(pkPayload, AppJsonContext.Default.JsonObject);
        var pk = KgCrypto.RsaEncryptNoPadding(pkJson).ToUpper();

        var body = new JsonObject
        {
            ["dfid"] = dfid,
            ["p3"] = p3Encrypted,
            ["plat"] = 1,
            ["t1"] = 0,
            ["t2"] = 0,
            ["t3"] = "MCwwLDAsMCwwLDAsMCwwLDA=",
            ["pk"] = pk,
            ["params"] = paramsEncrypted,
            ["userid"] = userid,
            ["clienttime_ms"] = dateNow
        };

        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            BaseUrl = ApiHost,
            Path = "/v4/login_by_token",
            SpecificRouter = LoginRouter,
            Body = body,
            SignatureType = SignatureType.Default
        };

        var response = await transport.SendAsync(request);

        try
        {
            var rootNode = JsonNode.Parse(response.GetRawText());
            if (rootNode is JsonObject rootObj &&
                rootObj["status"]?.GetValue<int>() == 1 &&
                rootObj["data"] is JsonObject dataObj)
                if (dataObj["secu_params"] is JsonValue secuVal)
                    try
                    {
                        var decryptedJson = KgCrypto.AesDecrypt(
                            secuVal.ToString(),
                            fixedKey
                        );
                        var decryptedNode = JsonNode.Parse(decryptedJson);
                        if (decryptedNode is JsonObject decryptedObj)
                            foreach (var kv in decryptedObj)
                                dataObj[kv.Key] = kv.Value?.DeepClone();

                        return JsonSerializer.SerializeToElement(rootNode);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[RawLoginApi] 解密/合并失败: {ex.Message}");
                        // 如果解密失败，就返回原始 response
                    }
        }
        catch
        {
            // 解析异常忽略，返回原样
        }

        return response;
    }
}