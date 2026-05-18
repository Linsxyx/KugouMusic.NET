using System.Text.RegularExpressions;

namespace KgWebApi.Net.Services;

public sealed partial class KgWebSessionMiddleware(RequestDelegate next)
{
    private static readonly Regex SessionKeyPattern = CreateSessionKeyPattern();

    public async Task InvokeAsync(HttpContext context, IKgWebSessionContext sessionContext)
    {
        var sessionKey = ResolveSessionKey(context);
        sessionContext.SessionKey = sessionKey;

        if (!IsPublicCachedEndpoint(context))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[KgWebSessionDefaults.HeaderName] = sessionKey;
                context.Response.Cookies.Append(
                    KgWebSessionDefaults.CookieName,
                    sessionKey,
                    new CookieOptions
                    {
                        HttpOnly = true,
                        IsEssential = true,
                        SameSite = SameSiteMode.Lax,
                        Secure = context.Request.IsHttps,
                        Expires = DateTimeOffset.UtcNow.AddDays(30)
                    });
                return Task.CompletedTask;
            });
        }

        await next(context);
    }

    private static bool IsPublicCachedEndpoint(HttpContext context)
    {
        return context.GetEndpoint()?.Metadata.GetMetadata<KgWebApi.Net.Controllers.KgPublicResponseCacheAttribute>() is not null;
    }

    private static string ResolveSessionKey(HttpContext context)
    {
        var headerValue = context.Request.Headers[KgWebSessionDefaults.HeaderName].ToString();
        if (IsValidSessionKey(headerValue)) return headerValue;

        if (context.Request.Cookies.TryGetValue(KgWebSessionDefaults.CookieName, out var cookieValue) &&
            IsValidSessionKey(cookieValue))
            return cookieValue;

        return Guid.NewGuid().ToString("N");
    }

    private static bool IsValidSessionKey(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Length <= 128 &&
               SessionKeyPattern.IsMatch(value);
    }

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex CreateSessionKeyPattern();
}
