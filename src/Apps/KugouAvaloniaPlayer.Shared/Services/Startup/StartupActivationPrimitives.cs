using System;
using System.Text.Json.Serialization;

namespace KugouAvaloniaPlayer.Services.Startup;

internal static class StartupActivationConstants
{
    public const string MutexName = "KugouAvaloniaPlayer.SingleInstance";
    public const string PipeName = "KugouAvaloniaPlayer.StartupActivation";
    public const string ActivateKind = "activate";

    public const int ForwardRetryCount = 5;
    public const int ConnectTimeoutMilliseconds = 250;
    public const int RetryDelayMilliseconds = 150;
}

internal enum StartupInstanceLaunchResult
{
    LaunchAsPrimary,
    ForwardedToPrimary,
    LaunchAsPrimaryAfterForwardRetryFailure
}

internal sealed record StartupActivationRequest(string Kind, string[] Args)
{
    public static StartupActivationRequest CreateActivate(string[] args)
    {
        return new StartupActivationRequest(
            StartupActivationConstants.ActivateKind,
            args ?? Array.Empty<string>());
    }
}

[JsonSerializable(typeof(StartupActivationRequest))]
internal partial class StartupActivationJsonContext : JsonSerializerContext
{
}
