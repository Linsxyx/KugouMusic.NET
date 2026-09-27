using KuGou.Net.Abstractions.Models;
using KuGou.Net.Adapters.Common;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.util;

namespace KuGou.Net.Clients;

/// <summary>Security challenges are completed interactively by the consuming application.</summary>
public sealed class VerificationClient(RawVerificationApi rawApi)
{
    public async Task<VerificationInfo?> GetInfoAsync(string eventId) =>
        KgApiResponseParser.Parse(await rawApi.GetInfoAsync(eventId), AppJsonContext.Default.VerificationInfo);

    public async Task<VerificationResult?> SubmitAsync(string eventId, int verificationType,
        string verifyCode, string sid, string edt) =>
        KgApiResponseParser.Parse(await rawApi.SubmitAsync(eventId, verificationType, verifyCode, sid, edt),
            AppJsonContext.Default.VerificationResult);
}
