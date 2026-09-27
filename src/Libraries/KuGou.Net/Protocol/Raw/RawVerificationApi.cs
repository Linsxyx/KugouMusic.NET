using System.Text.Json;
using System.Text.Json.Nodes;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Session;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

namespace KuGou.Net.Protocol.Raw;

public sealed class RawVerificationApi(IKgTransport transport, KgSessionManager sessionManager)
{
    public Task<JsonElement> GetInfoAsync(string eventId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        var body = CreateBody(eventId);
        body["rtype"] = 1;
        body["sid"] = "";
        body["edt"] = "";
        return transport.SendAsync(new KgRequest
        {
            Path = "/verifyservice/v3/get_verify_info",
            Method = HttpMethod.Post,
            Body = body
        });
    }

    /// <summary>Accepts unescaped values; URL decoding belongs to the HTTP adapter, not the SDK.</summary>
    public Task<JsonElement> SubmitAsync(string eventId, int verificationType, string verifyCode, string sid, string edt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(verifyCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(sid);
        ArgumentException.ThrowIfNullOrWhiteSpace(edt);
        if (verificationType is not (23 or 32))
            throw new ArgumentOutOfRangeException(nameof(verificationType), "不支持的安全验证类型。");

        var encryptedBody = new JsonObject();
        if (verificationType == 32)
            encryptedBody["code"] = verifyCode;
        var (encrypted, key) = KgCrypto.AesEncrypt(
            JsonSerializer.Serialize(encryptedBody, AppJsonContext.Default.JsonObject));
        var keyBody = new JsonObject { ["key"] = key };
        var body = CreateBody(eventId);
        body["v_type"] = verificationType;
        body["sid"] = sid;
        body["edt"] = edt;
        body[verificationType == 23 ? "verifycode" : "code"] = verifyCode;
        body["pk"] = KgCrypto.RsaEncryptNoPadding(JsonSerializer.Serialize(keyBody, AppJsonContext.Default.JsonObject));
        body["params"] = encrypted;
        return transport.SendAsync(new KgRequest
        {
            BaseUrl = "https://verifyservice.kugou.com",
            Path = "/v4/verify_user_info",
            Method = HttpMethod.Post,
            Params = new Dictionary<string, string> { ["clientver"] = "11510" },
            Body = body
        });
    }

    private JsonObject CreateBody(string eventId) => new()
    {
        ["eventid"] = eventId,
        ["userid"] = long.TryParse(sessionManager.Session.UserId, out var userId) ? userId : 0,
        ["platid"] = 2,
        ["wasm"] = 1,
        ["i"] = ""
    };
}
