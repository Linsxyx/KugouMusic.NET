using System;

namespace KugouAvaloniaPlayer.Models;

public enum UpdateCheckState { Current, Available, Failed, Unsupported }
public sealed record UpdateCheckResult(UpdateCheckState State, string Message)
{
    public static void RequireSuccessfulSource(int successfulSources)
    {
        if (successfulSources == 0) throw new InvalidOperationException("所有更新源均不可用。");
    }
}
