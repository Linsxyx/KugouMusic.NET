using System.Text;
using System.Text.Json;
using KuGou.Net.Protocol.Session;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

namespace KuGou.Net.Infrastructure.Http.Handlers;

public class KgSignatureHandler(KgSessionManager sessionManager) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!request.Options.TryGetValue(new HttpRequestOptionsKey<KgRequest>("KgRequestDetail"), out var kgReq))
            return await base.SendAsync(request, cancellationToken);

        var session = sessionManager.Session;
        var timeStr = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        // 1. 准备参数 (保持原有逻辑)
        //var currentDfid = kgReq.SpecificDfid ?? session.Dfid;
        var currentDfid = "-";
        var currentMid = KgUtils.CalcNewMid(currentDfid);
        var currentUuid = KgUtils.Md5(currentDfid + currentMid);

        var mergedParams = new Dictionary<string, string>(kgReq.Params);

        mergedParams.TryAdd("appid", KuGouConfig.AppId);
        mergedParams.TryAdd("clientver", KuGouConfig.ClientVer);
        mergedParams.TryAdd("dfid", currentDfid);
        mergedParams.TryAdd("mid", currentMid);
        mergedParams.TryAdd("uuid", currentUuid);
        if (!mergedParams.ContainsKey("userid")) mergedParams["userid"] = session.UserId;

        mergedParams.TryAdd("clienttime", timeStr);

        if (!mergedParams.ContainsKey("token") && !string.IsNullOrEmpty(session.Token))
            mergedParams["token"] = session.Token;

        if (kgReq.SignatureType == SignatureType.V5 && mergedParams.ContainsKey("hash"))
        {
            var paramMid = mergedParams.ContainsKey("mid") ? mergedParams["mid"] : currentMid;
            mergedParams["key"] = KgSigner.CalcV5Key(mergedParams["hash"], mergedParams["userid"], paramMid);
        }

        var jsonBody = "";
        if (request.Method == HttpMethod.Post && kgReq.Body != null && kgReq.Body.Count > 0)
        {
            jsonBody = JsonSerializer.Serialize(kgReq.Body, AppJsonContext.Default.JsonObject);

            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }
        else if (request.Method == HttpMethod.Post && !string.IsNullOrEmpty(kgReq.RawBody))
        {
            jsonBody = kgReq.RawBody;
        }

        var signature = "";
        if (kgReq.SignatureType == SignatureType.Web)
            signature = KgSigner.CalcWebQrSignature(mergedParams);
        else if (kgReq.SignatureType != SignatureType.None)

            signature = KgSigner.CalcPostSignature(mergedParams, jsonBody);

        if (!string.IsNullOrEmpty(signature)) mergedParams["signature"] = signature;


        var queryString = string.Join("&", mergedParams.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}"));
        var uriBuilder = new UriBuilder(request.RequestUri!) { Query = queryString };
        request.RequestUri = uriBuilder.Uri;


        if (!string.IsNullOrEmpty(kgReq.SpecificRouter))
            request.Headers.TryAddWithoutValidation("x-router", kgReq.SpecificRouter);

        request.Headers.TryAddWithoutValidation("User-Agent", KuGouConfig.UserAgent);
        request.Headers.TryAddWithoutValidation("dfid", currentDfid);
        request.Headers.TryAddWithoutValidation("mid", currentMid);
        request.Headers.TryAddWithoutValidation("clienttime", mergedParams["clienttime"]);
        
        request.Headers.TryAddWithoutValidation("kg-rc", "1");
        request.Headers.TryAddWithoutValidation("kg-thash", "5d816a0");
        request.Headers.TryAddWithoutValidation("kg-rec", "1");
        request.Headers.TryAddWithoutValidation("kg-rf", "B9EDA08A64250DEFFBCADDEE00F8F25F");

        return await base.SendAsync(request, cancellationToken);
    }
}