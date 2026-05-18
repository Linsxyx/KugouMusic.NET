using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace KgWebApi.Net.Controllers;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class KgPublicResponseCacheAttribute : Attribute, IOutputCachePolicy
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var request = context.HttpContext.Request;
        var isGet = HttpMethods.IsGet(request.Method);

        context.EnableOutputCaching = isGet;
        context.AllowCacheLookup = isGet;
        context.AllowCacheStorage = isGet;
        context.AllowLocking = isGet;
        context.ResponseExpirationTimeSpan = CacheDuration;
        context.CacheVaryByRules.QueryKeys = new StringValues("*");

        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        if (response.StatusCode != StatusCodes.Status200OK || !IsJsonResponse(response))
            context.AllowCacheStorage = false;

        return ValueTask.CompletedTask;
    }

    private static bool IsJsonResponse(HttpResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.ContentType))
            return false;

        if (!MediaTypeHeaderValue.TryParse(response.ContentType, out var contentType))
            return false;

        var mediaType = contentType.MediaType.Value ?? string.Empty;
        return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
               mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }
}
