using System.Net;

namespace KuGou.Net.Infrastructure.Http;

/// <summary>A verification header was returned without a usable JSON response.</summary>
public sealed class KgVerificationRequiredException(string ssaCode, HttpStatusCode statusCode)
    : Exception("酷狗要求安全验证，但未返回有效的 JSON 响应。")
{
    public string SsaCode { get; } = ssaCode;
    public HttpStatusCode StatusCode { get; } = statusCode;
}
