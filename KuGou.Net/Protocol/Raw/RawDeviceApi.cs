using System.Text.Json;
using System.Text.Json.Nodes;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Session;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

namespace KuGou.Net.Protocol.Raw;

public class RawDeviceApi(IKgTransport transport, KgSessionManager sessionManager)
{
    /// <summary>
    ///     [Update] 注册设备获取 DFID (V2 接口)
    /// </summary>
    public async Task<JsonElement> RegisterDevAsync(string userId, string token)
    {
        var session = sessionManager.Session;
        var clientTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 1. 构建虚拟硬件信息 (完全复刻 JS 的 dataMap)
        // 注意：imei 和 uuid 必须使用 Session 中的 InstallGuid
        var hardwareInfo = new JsonObject
        {
            ["availableRamSize"] = 4983533568L, 
            ["availableRomSize"] = 48114719L,
            ["availableSDSize"] = 48114717L,
            ["basebandVer"] = "",
            ["batteryLevel"] = 100,
            ["batteryStatus"] = 3,
            ["brand"] = "Redmi",
            ["buildSerial"] = "unknown",
            ["device"] = "marble", 
            ["imei"] = session.InstallGuid,
            ["imsi"] = "",
            ["manufacturer"] = "Xiaomi",
            ["uuid"] = session.InstallGuid, 
            ["accelerometer"] = false,
            ["accelerometerValue"] = "",
            ["gravity"] = false,
            ["gravityValue"] = "",
            ["gyroscope"] = false,
            ["gyroscopeValue"] = "",
            ["light"] = false,
            ["lightValue"] = "",
            ["magnetic"] = false,
            ["magneticValue"] = "",
            ["orientation"] = false,
            ["orientationValue"] = "",
            ["pressure"] = false,
            ["pressureValue"] = "",
            ["step_counter"] = false,
            ["step_counterValue"] = "",
            ["temperature"] = false,
            ["temperatureValue"] = ""
        };

        // 2. AES 加密 Body
        // 复用歌单模块的加密逻辑 (PlaylistAesEncrypt)
        var (aesStr, aesKey) = KgCrypto.PlaylistAesEncrypt(hardwareInfo);

        // 3. RSA 加密 Key (构造 p 参数)
        // JS: rsaEncrypt2({ aes: aesEncrypt.key, uid: userid, token })
        // 注意：这里使用的是 PKCS1 Padding (rsaEncrypt2)，不是 Login 时的 NoPadding
        var pData = new JsonObject
        {
            ["aes"] = aesKey,
            ["uid"] = userId,
            ["token"] = token
        };
        var pJson = JsonSerializer.Serialize(pData, AppJsonContext.Default.JsonObject);
        var p = KgCrypto.RsaEncryptPkcs1(pJson).ToUpper();

        // 4. 构造请求
        var request = new KgRequest
        {
            Method = HttpMethod.Post,
            BaseUrl = "https://userservice.kugou.com",
            Path = "/risk/v2/r_register_dev",
            Params = new Dictionary<string, string>
            {
                { "part", "1" },
                { "platid", "1" },
                { "p", p },
                // 下面这些参数 JS 的 useAxios 默认会带上，建议加上以防万一
                { "clientver", KuGouConfig.ClientVer },
                { "clienttime", clientTime.ToString() },
                { "appid", KuGouConfig.AppId }
            },
            RawBody = aesStr, // 直接发送 AES 加密后的 Base64 字符串
            ContentType = "text/plain", // 通常这种加密体也是 plain text
            SignatureType = SignatureType.Default,

            // 注册时还没有 DFID，Header 里的 dfid 设为 "-"
            SpecificDfid = "-"
        };

        var response = await transport.SendAsync(request);

        // 5. 解密响应
        // 响应是加密的字符串 (在 Transport 层可能被包在 __raw_base64__ 里)
        return TryDecryptResponse(response, aesKey);
    }

    private JsonElement TryDecryptResponse(JsonElement response, string aesKey)
    {
        try
        {
            string? encryptedContent = null;

            // 处理 Transport 层返回的几种可能格式
            if (response.ValueKind == JsonValueKind.Object &&
                response.TryGetProperty("__raw_base64__", out var raw))
                encryptedContent = raw.GetString();
            else if (response.ValueKind == JsonValueKind.String) encryptedContent = response.GetString();

            if (!string.IsNullOrEmpty(encryptedContent))
            {
                // 使用相同的 Key 解密
                var decryptedJson = KgCrypto.PlaylistAesDecrypt(encryptedContent, aesKey);

                // 解析解密后的 JSON
                using var doc = JsonDocument.Parse(decryptedJson);
                return doc.RootElement.Clone();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RawDeviceApi] 解密失败: {ex.Message}");
        }

        return response;
    }
}