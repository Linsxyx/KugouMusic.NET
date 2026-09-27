using System;
using System.Threading;
using System.Threading.Tasks;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Infrastructure.Http;

namespace KugouAvaloniaPlayer.Services;

internal static class SecurityVerifiedRequest
{
    public static async Task<T?> ExecuteAsync<T>(Func<Task<T?>> operation,
        Func<string, CancellationToken, Task<bool>> verify, CancellationToken token) where T : KgBaseModel
    {
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            T? result = null;
            string? eventId;
            try
            {
                result = await operation();
                eventId = result?.SsaCode;
            }
            catch (KgVerificationRequiredException ex)
            {
                eventId = ex.SsaCode;
            }
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(eventId))
            {
                if (result?.ErrorCode == 20028)
                    throw new InvalidOperationException("酷狗要求安全验证，但未返回验证标识，请稍后重试。");
                return result;
            }
            if (attempt != 0)
                throw new InvalidOperationException("验证后酷狗仍要求安全验证，请稍后重试。");
            if (!await verify(eventId, token))
                throw new OperationCanceledException("用户取消了安全验证。");
        }
    }
}
